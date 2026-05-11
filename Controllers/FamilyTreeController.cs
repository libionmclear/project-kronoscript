using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// Family Tree — premium-tagged drag/drop canvas. Each member has
/// their own tree composed of (a) PersonProfiles they created and
/// (b) Kronoscript members in their network. Relationships are simple:
/// Parent (directed) and Spouse (symmetric).
///
/// While premium enforcement is dormant the gates here are no-ops
/// (IPremiumService returns true for everyone). When the flag flips
/// on, free users keep read access to whatever they already built;
/// mutations require the FamilyTree feature.
/// </summary>
[Authorize]
public class FamilyTreeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPremiumService _premium;
    private readonly IFriendService _friends;

    public FamilyTreeController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IPremiumService premium,
        IFriendService friends)
    {
        _db = db;
        _userManager = userManager;
        _premium = premium;
        _friends = friends;
    }

    // GET: /FamilyTree
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);

        // Entry-point gate — if FamilyTree is in Off mode for this
        // viewer, bounce home. Admins always pass via IPremiumService.
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree))
        {
            TempData["Info"] = "The family tree isn't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var nodes = await _db.FamilyTreeNodes
            .Where(n => n.OwnerUserId == userId)
            .Include(n => n.TargetUser)
            .Include(n => n.TargetProfile)
            .ToListAsync();

        var edges = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == userId)
            .ToListAsync();

        // Available targets to add: own PersonProfiles (any visibility,
        // they're the creator) + members in their friend list + self.
        var profiles = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync();

        var friendList = await _friends.GetFriendListAsync(userId);
        var members = friendList.Friends
            .Select(f => f.User)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .ToList();

        var existingProfileIds = nodes
            .Where(n => n.NodeKind == FamilyNodeKind.Profile && n.TargetProfileId.HasValue)
            .Select(n => n.TargetProfileId!.Value)
            .ToHashSet();
        var existingMemberIds = nodes
            .Where(n => n.NodeKind == FamilyNodeKind.Member && !string.IsNullOrEmpty(n.TargetUserId))
            .Select(n => n.TargetUserId!)
            .ToHashSet();

        ViewBag.AvailableProfiles = profiles.Where(p => !existingProfileIds.Contains(p.Id)).ToList();
        ViewBag.AvailableMembers  = members.Where(m => !existingMemberIds.Contains(m.Id)).ToList();
        ViewBag.SelfPlaced = existingMemberIds.Contains(userId);
        ViewBag.CanMutate = await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree);
        ViewBag.Edges = edges;
        ViewBag.Self = user;

        return View(nodes);
    }

    // Snap new nodes onto a coarse grid so the canvas isn't a pile on
    // (0,0) when the user adds several at once.
    private async Task<(double X, double Y)> NextSlotAsync(string userId)
    {
        var count = await _db.FamilyTreeNodes.CountAsync(n => n.OwnerUserId == userId);
        const double colW = 160;
        const double rowH = 180;
        const int cols = 6;
        double x = 60 + (count % cols) * colW;
        double y = 60 + (count / cols) * rowH;
        return (x, y);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSelf()
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        var exists = await _db.FamilyTreeNodes.AnyAsync(n =>
            n.OwnerUserId == userId && n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == userId);
        if (!exists)
        {
            var (x, y) = await NextSlotAsync(userId);
            _db.FamilyTreeNodes.Add(new FamilyTreeNode
            {
                OwnerUserId = userId,
                NodeKind = FamilyNodeKind.Member,
                TargetUserId = userId,
                X = x, Y = y
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(string targetUserId)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        if (string.IsNullOrEmpty(targetUserId)) return BadRequest();

        // Authorize: must be the owner themselves or in their friend list.
        if (targetUserId != userId)
        {
            var fl = await _friends.GetFriendListAsync(userId);
            if (!fl.Friends.Any(f => f.User.Id == targetUserId))
            {
                return Forbid();
            }
        }

        var exists = await _db.FamilyTreeNodes.AnyAsync(n =>
            n.OwnerUserId == userId && n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == targetUserId);
        if (!exists)
        {
            var (x, y) = await NextSlotAsync(userId);
            _db.FamilyTreeNodes.Add(new FamilyTreeNode
            {
                OwnerUserId = userId,
                NodeKind = FamilyNodeKind.Member,
                TargetUserId = targetUserId,
                X = x, Y = y
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProfile(int profileId)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;

        // Must be a profile this user created — viewing-only profiles
        // can be tagged in stories but not pinned onto someone else's tree.
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
        if (profile == null) return NotFound();
        if (profile.CreatorUserId != userId) return Forbid();

        var exists = await _db.FamilyTreeNodes.AnyAsync(n =>
            n.OwnerUserId == userId && n.NodeKind == FamilyNodeKind.Profile && n.TargetProfileId == profileId);
        if (!exists)
        {
            var (x, y) = await NextSlotAsync(userId);
            _db.FamilyTreeNodes.Add(new FamilyTreeNode
            {
                OwnerUserId = userId,
                NodeKind = FamilyNodeKind.Profile,
                TargetProfileId = profileId,
                X = x, Y = y
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Move([FromBody] MoveDto dto)
    {
        if (dto == null) return BadRequest();
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        var node = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n => n.Id == dto.NodeId && n.OwnerUserId == userId);
        if (node == null) return NotFound();

        // Clamp coordinates to a sane world bound to keep nodes findable
        // even if the client sends nonsense.
        node.X = Math.Clamp(dto.X, 0, 4000);
        node.Y = Math.Clamp(dto.Y, 0, 4000);
        node.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok();
    }

    public class MoveDto
    {
        public int NodeId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int nodeId)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        var node = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n => n.Id == nodeId && n.OwnerUserId == userId);
        if (node == null) return NotFound();

        // Cascade edges referencing this node so the canvas doesn't draw
        // orphan lines.
        var dependentEdges = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == userId && (r.FromNodeId == nodeId || r.ToNodeId == nodeId))
            .ToListAsync();
        _db.FamilyRelationships.RemoveRange(dependentEdges);
        _db.FamilyTreeNodes.Remove(node);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRelationship(int fromNodeId, int toNodeId, FamilyRelationType relType)
    {
        if (!await GateAsync()) return Forbid();
        if (fromNodeId == toNodeId)
        {
            TempData["Error"] = "A node can't be related to itself.";
            return RedirectToAction(nameof(Index));
        }

        var userId = _userManager.GetUserId(User)!;
        var fromExists = await _db.FamilyTreeNodes.AnyAsync(n => n.Id == fromNodeId && n.OwnerUserId == userId);
        var toExists   = await _db.FamilyTreeNodes.AnyAsync(n => n.Id == toNodeId   && n.OwnerUserId == userId);
        if (!fromExists || !toExists) return NotFound();

        // Duplicate guard. For Spouse (symmetric) also reject the
        // reversed pair so (A→B) and (B→A) don't both exist.
        bool dupe;
        if (relType == FamilyRelationType.Spouse)
        {
            dupe = await _db.FamilyRelationships.AnyAsync(r =>
                r.OwnerUserId == userId && r.RelType == FamilyRelationType.Spouse &&
                ((r.FromNodeId == fromNodeId && r.ToNodeId == toNodeId) ||
                 (r.FromNodeId == toNodeId && r.ToNodeId == fromNodeId)));
        }
        else
        {
            dupe = await _db.FamilyRelationships.AnyAsync(r =>
                r.OwnerUserId == userId && r.RelType == relType &&
                r.FromNodeId == fromNodeId && r.ToNodeId == toNodeId);
        }
        if (dupe)
        {
            TempData["Error"] = "That relationship already exists.";
            return RedirectToAction(nameof(Index));
        }

        _db.FamilyRelationships.Add(new FamilyRelationship
        {
            OwnerUserId = userId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            RelType = relType
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRelationship(int relId)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        var rel = await _db.FamilyRelationships.FirstOrDefaultAsync(r => r.Id == relId && r.OwnerUserId == userId);
        if (rel == null) return NotFound();
        _db.FamilyRelationships.Remove(rel);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> GateAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree);
    }
}
