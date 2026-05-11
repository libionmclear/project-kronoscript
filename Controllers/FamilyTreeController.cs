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

    // Grid the tree snaps to — every node position the controller writes
    // is a multiple of (ColW, RowH) offset by GridOrigin so the canvas
    // looks like a tidy lattice instead of a free-form pile.
    private const double ColW = 160;
    private const double RowH = 180;
    private const double GridOrigin = 60;

    private static double SnapAxis(double v, double step) =>
        Math.Round((v - GridOrigin) / step) * step + GridOrigin;
    private static (double X, double Y) Snap(double x, double y) =>
        (SnapAxis(x, ColW), SnapAxis(y, RowH));

    // Snap new nodes onto a coarse grid so the canvas isn't a pile on
    // (0,0) when the user adds several at once.
    private async Task<(double X, double Y)> NextSlotAsync(string userId)
    {
        var count = await _db.FamilyTreeNodes.CountAsync(n => n.OwnerUserId == userId);
        const int cols = 6;
        double x = GridOrigin + (count % cols) * ColW;
        double y = GridOrigin + (count / cols) * RowH;
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

        // Clamp + snap. Even if the client snaps before sending (which it
        // does), the server snaps again so two slightly-off clients can't
        // create rows that are 5px misaligned forever.
        var clampedX = Math.Clamp(dto.X, 0, 4000);
        var clampedY = Math.Clamp(dto.Y, 0, 4000);
        var (sx, sy) = Snap(clampedX, clampedY);
        node.X = sx;
        node.Y = sy;
        node.UpdatedAt = DateTime.UtcNow;

        // If this node has a spouse, the spouse rides along sideways so
        // the pair stays glued. Spouse-of-spouse is symmetric in the
        // schema, so check both directions.
        var spouseEdge = await _db.FamilyRelationships
            .FirstOrDefaultAsync(r => r.OwnerUserId == userId
                                      && r.RelType == FamilyRelationType.Spouse
                                      && (r.FromNodeId == node.Id || r.ToNodeId == node.Id));
        if (spouseEdge != null)
        {
            var spouseId = spouseEdge.FromNodeId == node.Id ? spouseEdge.ToNodeId : spouseEdge.FromNodeId;
            var spouse = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n => n.Id == spouseId && n.OwnerUserId == userId);
            if (spouse != null)
            {
                // Keep whichever side the spouse was already on relative
                // to the moved node — flipping sides on every drag would
                // be jarring.
                var dx = spouse.X - node.X;
                var sideStepX = (dx >= 0 ? node.X + ColW : node.X - ColW);
                spouse.X = Math.Clamp(sideStepX, 0, 4000);
                spouse.Y = node.Y;
                spouse.UpdatedAt = DateTime.UtcNow;
            }
        }
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

        // Duplicate guard. For symmetric types (Spouse, Sibling) also
        // reject the reversed pair so (A→B) and (B→A) don't both exist.
        bool dupe;
        if (relType == FamilyRelationType.Spouse || relType == FamilyRelationType.Sibling)
        {
            dupe = await _db.FamilyRelationships.AnyAsync(r =>
                r.OwnerUserId == userId && r.RelType == relType &&
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

        // Auto-position so the canvas reads as a tree at a glance:
        //   - Spouse: glue the to-node directly to the right of the from-node.
        //   - Parent: keep the child at least one row below the parent.
        var fromNode = await _db.FamilyTreeNodes.FirstAsync(n => n.Id == fromNodeId && n.OwnerUserId == userId);
        var toNode   = await _db.FamilyTreeNodes.FirstAsync(n => n.Id == toNodeId   && n.OwnerUserId == userId);
        if (relType == FamilyRelationType.Spouse)
        {
            toNode.X = Math.Clamp(fromNode.X + ColW, 0, 4000);
            toNode.Y = fromNode.Y;
            toNode.UpdatedAt = DateTime.UtcNow;
        }
        else if (relType == FamilyRelationType.Parent)
        {
            if (toNode.Y < fromNode.Y + RowH)
            {
                toNode.Y = Math.Clamp(fromNode.Y + RowH, 0, 4000);
                toNode.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (relType == FamilyRelationType.Sibling)
        {
            // Place the new sibling on the same row as the existing one,
            // immediately to the right if there's space (or left if the
            // right is already crowded — checked cheaply, ignoring the
            // full row).
            toNode.Y = fromNode.Y;
            var preferredX = fromNode.X + ColW;
            // If there's already a node at the preferred slot, try the
            // other side. Either way, snap so we stay on grid.
            var occupiedRight = await _db.FamilyTreeNodes.AnyAsync(n =>
                n.OwnerUserId == userId && n.Id != toNode.Id &&
                Math.Abs(n.X - preferredX) < 1 && Math.Abs(n.Y - fromNode.Y) < 1);
            toNode.X = occupiedRight
                ? Math.Clamp(fromNode.X - ColW, 0, 4000)
                : Math.Clamp(preferredX, 0, 4000);
            toNode.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // POST: /FamilyTree/AutoArrange — single-shot tidy. Builds generation
    // numbers from Parent edges (root nodes = gen 0, each parent edge
    // adds one), then lays each generation out left-to-right with
    // spouses glued and siblings clustered. Doesn't try to be a real
    // genealogy renderer — just turns a pile into a readable grid.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoArrange()
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;

        var nodes = await _db.FamilyTreeNodes
            .Where(n => n.OwnerUserId == userId)
            .ToListAsync();
        if (nodes.Count == 0) return RedirectToAction(nameof(Index));

        var edges = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == userId)
            .ToListAsync();

        // Build parent → children. A node's generation is 1 + max(parents').
        // BFS from roots so we don't recurse on cycles (which shouldn't
        // exist but we don't want to crash if a user manages to create one).
        var parents = nodes.ToDictionary(n => n.Id, _ => new HashSet<int>());
        foreach (var e in edges.Where(r => r.RelType == FamilyRelationType.Parent))
        {
            if (parents.ContainsKey(e.ToNodeId)) parents[e.ToNodeId].Add(e.FromNodeId);
        }

        var gen = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (var n in nodes)
        {
            if (parents[n.Id].Count == 0)
            {
                gen[n.Id] = 0;
                queue.Enqueue(n.Id);
            }
        }
        var children = nodes.ToDictionary(n => n.Id, _ => new List<int>());
        foreach (var e in edges.Where(r => r.RelType == FamilyRelationType.Parent))
        {
            if (children.ContainsKey(e.FromNodeId)) children[e.FromNodeId].Add(e.ToNodeId);
        }
        // BFS — each child's gen is max(parent gens) + 1.
        var iterations = 0;
        while (queue.Count > 0 && iterations++ < nodes.Count * 4)
        {
            var id = queue.Dequeue();
            foreach (var c in children[id])
            {
                var newGen = parents[c].Select(p => gen.TryGetValue(p, out var g) ? g : 0).DefaultIfEmpty(0).Max() + 1;
                if (!gen.TryGetValue(c, out var cur) || cur < newGen)
                {
                    gen[c] = newGen;
                    queue.Enqueue(c);
                }
            }
        }
        // Any orphans the BFS didn't reach (cycles) — pin to gen 0.
        foreach (var n in nodes) if (!gen.ContainsKey(n.Id)) gen[n.Id] = 0;

        // Group spouses so they end up adjacent. Disjoint-set on Spouse edges.
        var parentOfSet = nodes.ToDictionary(n => n.Id, n => n.Id);
        int Find(int x) { while (parentOfSet[x] != x) { parentOfSet[x] = parentOfSet[parentOfSet[x]]; x = parentOfSet[x]; } return x; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parentOfSet[ra] = rb; }
        foreach (var e in edges.Where(r => r.RelType == FamilyRelationType.Spouse))
        {
            if (parentOfSet.ContainsKey(e.FromNodeId) && parentOfSet.ContainsKey(e.ToNodeId))
                Union(e.FromNodeId, e.ToNodeId);
        }

        // Lay out: for each generation, walk nodes in stable order, but
        // keep spouse pairs together by sorting within a generation
        // by (set root id, node id).
        var byGen = nodes.GroupBy(n => gen[n.Id]).OrderBy(g => g.Key);
        var clampMax = 2400 - 80;
        foreach (var grp in byGen)
        {
            var ordered = grp
                .OrderBy(n => Find(n.Id))
                .ThenBy(n => n.Id)
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var n = ordered[i];
                n.X = Math.Clamp(GridOrigin + i * ColW, 0, clampMax);
                n.Y = Math.Clamp(GridOrigin + grp.Key * RowH, 0, 4000);
                n.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Tree tidied — generations stacked, spouses glued.";
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
