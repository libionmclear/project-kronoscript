using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

/// <summary>
/// Owns the bookkeeping for the Family-plan multi-user coverage:
///   - up to 5 other users covered by one Family-plan owner
///   - covered users must be in the owner's Family-tier friend connections
///   - covered users get their PremiumUntil + PremiumTier copied from the
///     owner (denormalized so HasPremium() needs no JOIN)
///   - removing coverage / cancelling owner subscription clears those
///     fields on every covered member
/// </summary>
public interface IFamilyPlanService
{
    /// <summary>Max members the owner can cover (in addition to themselves).
    /// 5 + 1 owner = the 6 seats Family advertises.</summary>
    int MaxMembers { get; }

    /// <summary>True if the user is in an active Family-tier subscription.</summary>
    bool OwnsActiveFamilyPlan(ApplicationUser user);

    /// <summary>The users this owner is currently covering. Empty if none.</summary>
    Task<List<ApplicationUser>> GetCoveredAsync(string ownerId);

    /// <summary>The owner's Family-tier friend connections that are NOT yet
    /// covered — i.e., eligible to be added.</summary>
    Task<List<ApplicationUser>> GetEligibleAsync(string ownerId);

    /// <summary>Add a covered member. Validates that the owner has an active
    /// Family plan, the target is a Family-tier connection of the owner, and
    /// there are seats free. Stamps PremiumUntil + PremiumTier on the member.</summary>
    Task<(bool ok, string? error)> AddMemberAsync(string ownerId, string memberId);

    /// <summary>Remove a covered member. Clears their CoveredBy id + premium
    /// fields. No-op if they weren't covered by this owner.</summary>
    Task<bool> RemoveMemberAsync(string ownerId, string memberId);

    /// <summary>Re-sync every member's PremiumUntil from the owner — called by
    /// the Stripe webhook on subscription.updated / .renewed.</summary>
    Task SyncCoverageAsync(string ownerId);

    /// <summary>Clear coverage for every member of this owner — called by the
    /// Stripe webhook on subscription.deleted, OR when the owner downgrades
    /// from Family to Personal.</summary>
    Task ClearCoverageAsync(string ownerId);
}

public class FamilyPlanService : IFamilyPlanService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<FamilyPlanService> _log;

    public FamilyPlanService(ApplicationDbContext db, ILogger<FamilyPlanService> log)
    {
        _db = db;
        _log = log;
    }

    public int MaxMembers => 5;

    public bool OwnsActiveFamilyPlan(ApplicationUser user) =>
        user != null
        && user.PremiumTier == "Family"
        && user.PremiumUntil.HasValue
        && user.PremiumUntil.Value > DateTime.UtcNow;

    public async Task<List<ApplicationUser>> GetCoveredAsync(string ownerId) =>
        await _db.Users
            .Where(u => u.CoveredByFamilyPlanOwnerId == ownerId)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .ToListAsync();

    public async Task<List<ApplicationUser>> GetEligibleAsync(string ownerId)
    {
        // Family-tier friend connections, either side, status Accepted.
        var familyConnIds = await _db.FriendConnections
            .Where(c => c.Status == FriendConnectionStatus.Accepted
                        && c.Tier == FriendTier.Family
                        && (c.RequesterUserId == ownerId || c.AddresseeUserId == ownerId))
            .Select(c => c.RequesterUserId == ownerId ? c.AddresseeUserId : c.RequesterUserId)
            .ToListAsync();

        if (familyConnIds.Count == 0) return new List<ApplicationUser>();

        // Exclude already-covered, biographical, or themselves.
        return await _db.Users
            .Where(u => familyConnIds.Contains(u.Id)
                        && u.Id != ownerId
                        && u.CoveredByFamilyPlanOwnerId == null
                        && !u.IsBiographical)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .ToListAsync();
    }

    public async Task<(bool ok, string? error)> AddMemberAsync(string ownerId, string memberId)
    {
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        if (owner == null) return (false, "Owner not found.");
        if (!OwnsActiveFamilyPlan(owner)) return (false, "You don't have an active Family subscription.");

        var member = await _db.Users.FirstOrDefaultAsync(u => u.Id == memberId);
        if (member == null) return (false, "Member not found.");
        if (member.Id == ownerId) return (false, "That's you — the Family plan covers you automatically.");
        if (!string.IsNullOrEmpty(member.CoveredByFamilyPlanOwnerId))
            return (false, "That person is already covered by a Family plan.");

        // Must be a Family-tier connection of the owner.
        var isFamilyConn = await _db.FriendConnections.AnyAsync(c =>
            c.Status == FriendConnectionStatus.Accepted
            && c.Tier == FriendTier.Family
            && ((c.RequesterUserId == ownerId && c.AddresseeUserId == memberId)
             || (c.AddresseeUserId == ownerId && c.RequesterUserId == memberId)));
        if (!isFamilyConn)
            return (false, "That person isn't a Family-tier connection yet. Move them to Family in your network first.");

        // Seat limit.
        var coveredCount = await _db.Users.CountAsync(u => u.CoveredByFamilyPlanOwnerId == ownerId);
        if (coveredCount >= MaxMembers)
            return (false, $"Family plan is at its {MaxMembers}-seat limit. Remove someone first.");

        member.CoveredByFamilyPlanOwnerId = ownerId;
        member.PremiumTier   = "Family";
        member.PremiumUntil  = owner.PremiumUntil;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<bool> RemoveMemberAsync(string ownerId, string memberId)
    {
        var member = await _db.Users.FirstOrDefaultAsync(u =>
            u.Id == memberId && u.CoveredByFamilyPlanOwnerId == ownerId);
        if (member == null) return false;
        member.CoveredByFamilyPlanOwnerId = null;
        // If they have no other premium signal (own subscription), clear it.
        if (string.IsNullOrEmpty(member.StripeSubscriptionId))
        {
            member.PremiumTier  = null;
            member.PremiumUntil = null;
        }
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task SyncCoverageAsync(string ownerId)
    {
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        if (owner == null) return;
        if (!OwnsActiveFamilyPlan(owner))
        {
            await ClearCoverageAsync(ownerId);
            return;
        }
        var members = await _db.Users
            .Where(u => u.CoveredByFamilyPlanOwnerId == ownerId)
            .ToListAsync();
        foreach (var m in members)
        {
            m.PremiumTier   = "Family";
            m.PremiumUntil  = owner.PremiumUntil;
        }
        await _db.SaveChangesAsync();
    }

    public async Task ClearCoverageAsync(string ownerId)
    {
        var members = await _db.Users
            .Where(u => u.CoveredByFamilyPlanOwnerId == ownerId)
            .ToListAsync();
        foreach (var m in members)
        {
            m.CoveredByFamilyPlanOwnerId = null;
            // Don't strip the member of premium they might have on their own.
            if (string.IsNullOrEmpty(m.StripeSubscriptionId))
            {
                m.PremiumTier  = null;
                m.PremiumUntil = null;
            }
        }
        await _db.SaveChangesAsync();
    }
}
