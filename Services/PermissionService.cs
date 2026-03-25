using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _db;

    public PermissionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<FriendTier?> GetViewerTierAsync(string viewerUserId, string profileOwnerUserId)
    {
        if (viewerUserId == profileOwnerUserId) return FriendTier.Family; // owner sees everything

        var connection = await _db.FriendConnections
            .Where(f => f.Status == FriendConnectionStatus.Accepted)
            .Where(f =>
                (f.RequesterUserId == viewerUserId && f.AddresseeUserId == profileOwnerUserId) ||
                (f.RequesterUserId == profileOwnerUserId && f.AddresseeUserId == viewerUserId))
            .FirstOrDefaultAsync();

        return connection?.Tier;
    }

    public async Task<bool> CanViewPostsAsync(string viewerUserId, string ownerUserId)
    {
        if (viewerUserId == ownerUserId) return true;
        var tier = await GetViewerTierAsync(viewerUserId, ownerUserId);
        return tier.HasValue; // any tier can view
    }

    public async Task<bool> CanCommentAsync(string viewerUserId, string ownerUserId)
    {
        if (viewerUserId == ownerUserId) return true;
        var tier = await GetViewerTierAsync(viewerUserId, ownerUserId);
        return tier is FriendTier.Friend or FriendTier.Family;
    }

    public async Task<bool> CanReorderAsync(string viewerUserId, string ownerUserId)
    {
        if (viewerUserId == ownerUserId) return true;
        var tier = await GetViewerTierAsync(viewerUserId, ownerUserId);
        return tier == FriendTier.Family;
    }
}
