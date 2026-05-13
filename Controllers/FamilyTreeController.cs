using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Helpers;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// Family Tree — auto-laid-out canvas. Bubbles can't be dragged; the
/// controller computes positions from the relationship graph on every
/// render so the tree always reads cleanly. The owner is auto-seeded
/// as a Member node on first visit and rendered at the horizontal
/// centre. Adding is done through two panels at the top of the page
/// (member / profile); the relation chosen determines where the new
/// bubble lands.
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

    // Bubble geometry — fixed; the layout works in these units.
    private const double BubbleW = 80;
    private const double BubbleH = 80;
    private const double ColGap  = 60;       // horizontal gap between sibling subtrees
    // Gap between two ADJACENT siblings at the same parent level. Smaller
    // than ColGap so kids pack tightly under their parents — kids are
    // visually a "row" of bubbles and a tight pack reads as a cohesive
    // group. ColGap stays as the anchor-to-anchor / disconnected-tree
    // padding where the bigger separator is intentional.
    private const double SiblingGap = 20;
    private const double RowGap  = 80;       // vertical gap between generations
    private const double RowH    = BubbleH + RowGap + 24; // include label height
    private const double ColW    = BubbleW + ColGap;      // horizontal cell step

    // GET: /FamilyTree
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);

        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree))
        {
            TempData["Info"] = "The family tree isn't available right now.";
            return RedirectToAction("Index", "Home");
        }

        // Auto-seed self. The owner is always present on their own tree —
        // they're the anchor everything else is laid out around.
        var selfExists = await _db.FamilyTreeNodes.AnyAsync(n =>
            n.OwnerUserId == userId && n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == userId);
        if (!selfExists)
        {
            _db.FamilyTreeNodes.Add(new FamilyTreeNode
            {
                OwnerUserId = userId,
                NodeKind = FamilyNodeKind.Member,
                TargetUserId = userId
            });
            await _db.SaveChangesAsync();
        }

        var nodes = await _db.FamilyTreeNodes
            .Where(n => n.OwnerUserId == userId)
            .Include(n => n.TargetUser)
            .Include(n => n.TargetProfile)
                .ThenInclude(p => p!.LinkedUser)
            .ToListAsync();

        var edges = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == userId)
            .ToListAsync();

        var layout = BuildLayout(nodes, edges, userId);

        // Pickers — two flavours:
        //   AvailableMembers / AvailableProfiles  → top "More options" forms,
        //       filtered to people NOT yet on the tree (those forms create a
        //       new bubble).
        //   PickableMembers   / PickableProfiles  → click-bubble popup, which
        //       just wires up relationships. Includes everyone (on tree or
        //       not) so a writer adding e.g. "Uncle Bob is Maria's spouse"
        //       can find Uncle Bob even after he's been placed; the controller
        //       reuses the existing node and only adds the edge.
        var friendList = await _friends.GetFriendListAsync(userId);
        var onTreeUserIds = nodes
            .Where(n => n.NodeKind == FamilyNodeKind.Member && !string.IsNullOrEmpty(n.TargetUserId))
            .Select(n => n.TargetUserId!).ToHashSet();
        var onTreeProfileIds = nodes
            .Where(n => n.NodeKind == FamilyNodeKind.Profile && n.TargetProfileId.HasValue)
            .Select(n => n.TargetProfileId!.Value).ToHashSet();

        var allFriends = friendList.Friends
            .Select(f => f.User)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .ToList();
        ViewBag.AvailableMembers = allFriends.Where(u => !onTreeUserIds.Contains(u.Id)).ToList();
        ViewBag.PickableMembers  = allFriends;

        // Own NPCs + NPCs from family-tier connections (mirrors the tag
        // widget pool) — that way "Pick from list" inside the popup
        // includes every NPC the user can legitimately attach to the tree.
        var familyIds = friendList.Friends
            .Where(f => f.Tier == FriendTier.Family)
            .Select(f => f.User.Id).ToList();
        var ownProfiles = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync();
        var familySharedProfiles = familyIds.Count == 0
            ? new List<PersonProfile>()
            : await _db.PersonProfiles
                .Where(p => familyIds.Contains(p.CreatorUserId) && p.Visibility != PostVisibility.Private)
                .OrderBy(p => p.DisplayName)
                .ToListAsync();
        var allPickableProfiles = ownProfiles.Concat(familySharedProfiles).ToList();
        ViewBag.AvailableProfiles = ownProfiles.Where(p => !onTreeProfileIds.Contains(p.Id)).ToList();
        ViewBag.PickableProfiles  = allPickableProfiles;
        ViewBag.OnTreeProfileIds  = onTreeProfileIds;
        ViewBag.OnTreeMemberIds   = onTreeUserIds;

        ViewBag.CanMutate = await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree);
        ViewBag.Self = user;
        ViewBag.Layout = layout;

        // Compute the kinship term from the owner ("self") to every
        // other bubble — so the bubble subtitle reads "Grandfather"
        // instead of the raw "Father" string the writer typed when they
        // placed the parent of a parent.
        var selfNode = nodes.FirstOrDefault(n =>
            n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == userId);
        var relations = new Dictionary<int, string>();
        if (selfNode != null)
        {
            var calc = new RelationshipCalculator(selfNode.Id, nodes, edges);
            foreach (var n in nodes)
            {
                if (n.Id == selfNode.Id) continue;
                var term = calc.Compute(n.Id);
                if (!string.IsNullOrEmpty(term)) relations[n.Id] = term;
            }
        }
        ViewBag.Relations = relations;

        // Map node → spouse-node so the "add child" picker can default
        // the second parent to whoever the first parent is married to.
        var spouseMap = new Dictionary<int, int>();
        foreach (var e in edges.Where(x => x.RelType == FamilyRelationType.Spouse))
        {
            spouseMap[e.FromNodeId] = e.ToNodeId;
            spouseMap[e.ToNodeId]   = e.FromNodeId;
        }
        ViewBag.SpouseMap = spouseMap;

        // Ancestor map: nodeId → full set of node ids in its ancestor
        // chain (transitive closure of Parent edges going UP). Used by
        // the client-side collapse feature: clicking "−" on a bubble
        // hides every node in its ancestor set.
        var parentEdgesAll = edges.Where(e => e.RelType == FamilyRelationType.Parent).ToList();
        var directParents = nodes.ToDictionary(n => n.Id, _ => new List<int>());
        foreach (var e in parentEdgesAll)
        {
            if (directParents.ContainsKey(e.ToNodeId)) directParents[e.ToNodeId].Add(e.FromNodeId);
        }
        var ancestorMap = new Dictionary<int, List<int>>();
        foreach (var n in nodes)
        {
            var anc = new HashSet<int>();
            var q = new Queue<int>(directParents[n.Id]);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                if (!anc.Add(p)) continue;
                if (directParents.TryGetValue(p, out var pp))
                    foreach (var ppId in pp) q.Enqueue(ppId);
            }
            ancestorMap[n.Id] = anc.ToList();
        }
        ViewBag.AncestorMap = ancestorMap;

        return View(nodes);
    }

    // ── Add a member (existing Kronoscript user) ────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(string targetUserId, int relationToNodeId, AddRelation relationKind, int? secondParentNodeId = null)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        if (string.IsNullOrEmpty(targetUserId)) return BadRequest();

        // Authorize: member must be self or in this user's friend list.
        if (targetUserId != userId)
        {
            var fl = await _friends.GetFriendListAsync(userId);
            if (!fl.Friends.Any(f => f.User.Id == targetUserId)) return Forbid();
        }

        var existing = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n =>
            n.OwnerUserId == userId && n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == targetUserId);
        var node = existing ?? new FamilyTreeNode
        {
            OwnerUserId = userId,
            NodeKind = FamilyNodeKind.Member,
            TargetUserId = targetUserId
        };
        if (existing == null)
        {
            _db.FamilyTreeNodes.Add(node);
            await _db.SaveChangesAsync(); // need Id for the edge
        }

        await CreateRelationshipAsync(userId, node, relationToNodeId, relationKind, secondParentNodeId);
        return RedirectToAction(nameof(Index));
    }

    // ── Add an existing People Profile ──────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProfile(int profileId, int relationToNodeId, AddRelation relationKind, int? secondParentNodeId = null)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;

        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
        if (profile == null) return NotFound();
        if (profile.CreatorUserId != userId) return Forbid();

        var existing = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n =>
            n.OwnerUserId == userId && n.NodeKind == FamilyNodeKind.Profile && n.TargetProfileId == profileId);
        var node = existing ?? new FamilyTreeNode
        {
            OwnerUserId = userId,
            NodeKind = FamilyNodeKind.Profile,
            TargetProfileId = profileId
        };
        if (existing == null)
        {
            _db.FamilyTreeNodes.Add(node);
            await _db.SaveChangesAsync();
        }

        await CreateRelationshipAsync(userId, node, relationToNodeId, relationKind, secondParentNodeId);
        return RedirectToAction(nameof(Index));
    }

    // ── Create a new People Profile inline AND add to tree ──────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProfileAndAdd(
        string displayName,
        string? nickname,
        string? relation,
        int? birthYear,
        int? deathYear,
        int relationToNodeId,
        AddRelation relationKind,
        int? secondParentNodeId = null,
        string? gender = null)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }

        // Gender — if the user didn't explicitly pick a value in the
        // popup, infer it from the kindLabel (Father → Male, Mother →
        // Female, …) so the kinship calculator reads the right term
        // without the user having to confirm every time.
        var resolvedGender = string.IsNullOrWhiteSpace(gender) ? null : gender.Trim();
        if (string.IsNullOrEmpty(resolvedGender))
        {
            var k = (relation ?? "").Trim().ToLowerInvariant();
            if (k == "father" || k == "son" || k == "brother") resolvedGender = "Male";
            else if (k == "mother" || k == "daughter" || k == "sister") resolvedGender = "Female";
        }

        var profile = new PersonProfile
        {
            CreatorUserId = userId,
            DisplayName = displayName.Trim(),
            Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim(),
            Gender = resolvedGender,
            Relation = string.IsNullOrWhiteSpace(relation) ? null : relation.Trim(),
            BirthYear = birthYear,
            DeathYear = deathYear,
            Visibility = PostVisibility.Family,
            CreatedAt = DateTime.UtcNow
        };
        try
        {
            _db.PersonProfiles.Add(profile);
            await _db.SaveChangesAsync();

            var node = new FamilyTreeNode
            {
                OwnerUserId = userId,
                NodeKind = FamilyNodeKind.Profile,
                TargetProfileId = profile.Id
            };
            _db.FamilyTreeNodes.Add(node);
            await _db.SaveChangesAsync();

            await CreateRelationshipAsync(userId, node, relationToNodeId, relationKind, secondParentNodeId);
        }
        catch (Exception ex)
        {
            // Surface the actual reason a "doesn't add" failure happens —
            // schema column missing on an older deploy, an unexpectedly-
            // long field, a relationship-anchor mismatch, etc. Without
            // this catch the request silently 500s and the user sees the
            // page reload with nothing new.
            TempData["Error"] = "Could not add to tree: " + (ex.InnerException?.Message ?? ex.Message);
        }
        return RedirectToAction(nameof(Index));
    }

    // ── Connect two existing nodes ──────────────────────────────────────
    // Lets the user wire up a person they added "floating" (no
    // relationship surfaced) — pick the two nodes + the relationship.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(int fromNodeId, int relationToNodeId, AddRelation relationKind, int? secondParentNodeId = null)
    {
        if (!await GateAsync()) return Forbid();
        if (fromNodeId == relationToNodeId)
        {
            TempData["Error"] = "Pick two different people.";
            return RedirectToAction(nameof(Index));
        }
        var userId = _userManager.GetUserId(User)!;
        var fromNode = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n => n.Id == fromNodeId && n.OwnerUserId == userId);
        if (fromNode == null) return NotFound();
        await CreateRelationshipAsync(userId, fromNode, relationToNodeId, relationKind, secondParentNodeId);
        TempData["Success"] = "Relationship added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int nodeId)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        var node = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n => n.Id == nodeId && n.OwnerUserId == userId);
        if (node == null) return NotFound();

        // Block removing self — the owner is the anchor for the whole layout.
        if (node.NodeKind == FamilyNodeKind.Member && node.TargetUserId == userId)
        {
            TempData["Error"] = "You're the centre of your own tree — you can't remove yourself.";
            return RedirectToAction(nameof(Index));
        }

        var deps = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == userId && (r.FromNodeId == nodeId || r.ToNodeId == nodeId))
            .ToListAsync();
        _db.FamilyRelationships.RemoveRange(deps);
        _db.FamilyTreeNodes.Remove(node);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> GateAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree);
    }

    // The choices the add forms expose. We map to FamilyRelationship rows.
    public enum AddRelation
    {
        Parent = 0,   // new person is parent of the existing node
        Child = 1,    // new person is child of the existing node (also gets edge to existing's spouse if any)
        Spouse = 2,   // new person marries the existing node
        Sibling = 3   // new person is a sibling of the existing node
    }

    private async Task CreateRelationshipAsync(string userId, FamilyTreeNode newNode, int relationToNodeId, AddRelation kind, int? secondParentNodeId = null)
    {
        var anchor = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n => n.Id == relationToNodeId && n.OwnerUserId == userId);
        if (anchor == null) return;

        switch (kind)
        {
            case AddRelation.Parent:
                if (!await EdgeExistsAsync(userId, newNode.Id, anchor.Id, FamilyRelationType.Parent))
                {
                    _db.FamilyRelationships.Add(new FamilyRelationship
                    {
                        OwnerUserId = userId,
                        FromNodeId = newNode.Id,
                        ToNodeId = anchor.Id,
                        RelType = FamilyRelationType.Parent
                    });
                }
                // Propagate to siblings: if the anchor has Sibling edges
                // to other nodes (Sylvia was added as Marco's sibling
                // before Mario was on the tree), the new parent is also
                // a parent of those siblings. Without this propagation
                // the kinship calculator falls back to "Relative" since
                // sibling-by-shared-parents only works once both share
                // a parent in the graph.
                var siblingIds = await _db.FamilyRelationships
                    .Where(r => r.OwnerUserId == userId
                                && r.RelType == FamilyRelationType.Sibling
                                && (r.FromNodeId == anchor.Id || r.ToNodeId == anchor.Id))
                    .Select(r => r.FromNodeId == anchor.Id ? r.ToNodeId : r.FromNodeId)
                    .ToListAsync();
                foreach (var sibId in siblingIds.Distinct())
                {
                    if (sibId == newNode.Id) continue;
                    if (!await EdgeExistsAsync(userId, newNode.Id, sibId, FamilyRelationType.Parent))
                    {
                        _db.FamilyRelationships.Add(new FamilyRelationship
                        {
                            OwnerUserId = userId,
                            FromNodeId = newNode.Id,
                            ToNodeId = sibId,
                            RelType = FamilyRelationType.Parent
                        });
                    }
                }
                // Auto-couple: if the anchor already has another parent on
                // the tree (and neither side has a different spouse yet),
                // wire the new parent + the existing one together as a
                // couple. Without this, the two parents lay out as two
                // unrelated anchors and one of them ends up stranded off
                // to the side instead of perched above the child with a
                // marriage line and a single drop.
                var existingOtherParents = await _db.FamilyRelationships
                    .Where(r => r.OwnerUserId == userId
                                && r.RelType == FamilyRelationType.Parent
                                && r.ToNodeId == anchor.Id
                                && r.FromNodeId != newNode.Id)
                    .Select(r => r.FromNodeId)
                    .ToListAsync();
                foreach (var otherParentId in existingOtherParents)
                {
                    if (await SymmetricEdgeExistsAsync(userId, newNode.Id, otherParentId, FamilyRelationType.Spouse))
                        continue;
                    var newSpouse = await GetSpouseNodeIdAsync(userId, newNode.Id);
                    var otherSpouse = await GetSpouseNodeIdAsync(userId, otherParentId);
                    if (newSpouse.HasValue   && newSpouse.Value   != otherParentId) continue;
                    if (otherSpouse.HasValue && otherSpouse.Value != newNode.Id)    continue;
                    _db.FamilyRelationships.Add(new FamilyRelationship
                    {
                        OwnerUserId = userId,
                        FromNodeId = newNode.Id,
                        ToNodeId = otherParentId,
                        RelType = FamilyRelationType.Spouse
                    });
                    break; // pair with the first eligible existing parent
                }
                break;
            case AddRelation.Child:
                // First parent (the primary anchor the user chose).
                if (!await EdgeExistsAsync(userId, anchor.Id, newNode.Id, FamilyRelationType.Parent))
                {
                    _db.FamilyRelationships.Add(new FamilyRelationship
                    {
                        OwnerUserId = userId,
                        FromNodeId = anchor.Id,
                        ToNodeId = newNode.Id,
                        RelType = FamilyRelationType.Parent
                    });
                }
                // Second parent — explicitly chosen by the user in the
                // form (so single-parent setups can be expressed too).
                // We no longer infer the spouse silently; that surprised
                // users when the spouse on the tree wasn't the actual
                // parent of this particular child.
                if (secondParentNodeId.HasValue && secondParentNodeId.Value != anchor.Id)
                {
                    var second = await _db.FamilyTreeNodes.FirstOrDefaultAsync(n =>
                        n.Id == secondParentNodeId.Value && n.OwnerUserId == userId);
                    if (second != null
                        && !await EdgeExistsAsync(userId, second.Id, newNode.Id, FamilyRelationType.Parent))
                    {
                        _db.FamilyRelationships.Add(new FamilyRelationship
                        {
                            OwnerUserId = userId,
                            FromNodeId = second.Id,
                            ToNodeId = newNode.Id,
                            RelType = FamilyRelationType.Parent
                        });
                    }
                }
                break;
            case AddRelation.Spouse:
                if (!await SymmetricEdgeExistsAsync(userId, newNode.Id, anchor.Id, FamilyRelationType.Spouse))
                {
                    _db.FamilyRelationships.Add(new FamilyRelationship
                    {
                        OwnerUserId = userId,
                        FromNodeId = anchor.Id,
                        ToNodeId = newNode.Id,
                        RelType = FamilyRelationType.Spouse
                    });
                }
                break;
            case AddRelation.Sibling:
                // If the anchor has parents on the tree, attach the new
                // sibling to those same parents (semantically cleaner —
                // siblings via shared parents). Otherwise drop a Sibling
                // edge so the relationship is at least expressed.
                var parentIds = await _db.FamilyRelationships
                    .Where(r => r.OwnerUserId == userId && r.RelType == FamilyRelationType.Parent && r.ToNodeId == anchor.Id)
                    .Select(r => r.FromNodeId)
                    .ToListAsync();
                if (parentIds.Count > 0)
                {
                    foreach (var pid in parentIds)
                    {
                        if (!await EdgeExistsAsync(userId, pid, newNode.Id, FamilyRelationType.Parent))
                        {
                            _db.FamilyRelationships.Add(new FamilyRelationship
                            {
                                OwnerUserId = userId,
                                FromNodeId = pid,
                                ToNodeId = newNode.Id,
                                RelType = FamilyRelationType.Parent
                            });
                        }
                    }
                }
                else if (!await SymmetricEdgeExistsAsync(userId, newNode.Id, anchor.Id, FamilyRelationType.Sibling))
                {
                    _db.FamilyRelationships.Add(new FamilyRelationship
                    {
                        OwnerUserId = userId,
                        FromNodeId = anchor.Id,
                        ToNodeId = newNode.Id,
                        RelType = FamilyRelationType.Sibling
                    });
                }
                break;
        }
        await _db.SaveChangesAsync();
    }

    private Task<bool> EdgeExistsAsync(string userId, int from, int to, FamilyRelationType type) =>
        _db.FamilyRelationships.AnyAsync(r =>
            r.OwnerUserId == userId && r.RelType == type && r.FromNodeId == from && r.ToNodeId == to);

    private Task<bool> SymmetricEdgeExistsAsync(string userId, int a, int b, FamilyRelationType type) =>
        _db.FamilyRelationships.AnyAsync(r =>
            r.OwnerUserId == userId && r.RelType == type &&
            ((r.FromNodeId == a && r.ToNodeId == b) || (r.FromNodeId == b && r.ToNodeId == a)));

    private async Task<int?> GetSpouseNodeIdAsync(string userId, int nodeId)
    {
        var spouseEdge = await _db.FamilyRelationships
            .FirstOrDefaultAsync(r => r.OwnerUserId == userId && r.RelType == FamilyRelationType.Spouse
                                      && (r.FromNodeId == nodeId || r.ToNodeId == nodeId));
        if (spouseEdge == null) return null;
        return spouseEdge.FromNodeId == nodeId ? spouseEdge.ToNodeId : spouseEdge.FromNodeId;
    }

    // ────────────────────────────────────────────────────────────────────
    // Layout: build couple units, recursive width pass, recursive position
    // pass, then shift everything horizontally so self is at canvas centre.
    // ────────────────────────────────────────────────────────────────────

    public class TreeLayout
    {
        public List<PositionedNode> Nodes { get; set; } = new();
        public List<MarriageLine> Marriages { get; set; } = new();
        public List<ChildBranch> ChildBranches { get; set; } = new();
        public List<SiblingLine> Siblings { get; set; } = new();
        public List<TreePlaceholder> Placeholders { get; set; } = new();
        public List<SecondaryParentLink> SecondaryParents { get; set; } = new();
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public int? SelfNodeId { get; set; }
    }
    /// <summary>Connector line from a couple (the second set of parents
    /// of a child) down to the child's bubble — needed when the child's
    /// couple unit was already claimed by the FIRST set of parents in
    /// the descendant tree, leaving the second set as a floating anchor.
    /// Rendered as a bent path: down, across, down.</summary>
    public class SecondaryParentLink
    {
        public double FromX { get; set; }   // anchor couple midpoint
        public double FromY { get; set; }   // anchor couple bottom-edge Y
        public double ToX { get; set; }     // target child node center X
        public double ToY { get; set; }     // target child node top Y
    }
    public class TreePlaceholder
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Label { get; set; } = "";        // "Father" / "Mother" / "Spouse" / …
        public string Icon { get; set; } = "+";
        public int AnchorNodeId { get; set; }
        public string AnchorName { get; set; } = "";
        public string RelationKind { get; set; } = ""; // "Parent" / "Spouse" / "Child" / "Sibling"
        public string KindLabel { get; set; } = "";    // "Father" / "Mother" / …
    }
    public class PositionedNode
    {
        public FamilyTreeNode Node { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
    }
    public class MarriageLine
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y { get; set; }
        // Endpoints carry the two node ids so the view can wire a
        // hover-"+ Add child" button at the line's midpoint that opens
        // the popup pre-filled with both parents.
        public int LeftNodeId { get; set; }
        public int RightNodeId { get; set; }
        // Drawn dashed instead of solid when this is a SECONDARY
        // marriage (a second/third spouse rendered adjacent to a hub
        // who already has a primary partner). Solid by default.
        public bool IsAdditional { get; set; }
    }
    public class ChildBranch
    {
        // Vertical drop from the parent/couple line down to the children's branch line.
        public double DropX { get; set; }
        public double DropY1 { get; set; }
        public double DropY2 { get; set; }
        // Horizontal branch across all children's top stems.
        public double BranchX1 { get; set; }
        public double BranchX2 { get; set; }
        public double BranchY { get; set; }
        // Per-child short vertical stem from branch down to bubble top.
        // ChildNodeId is the node id whose bubble this stem lands on —
        // used by client-side collapse so stems hidden along with their
        // child bubble disappear together.
        public List<(double X, double Y1, double Y2, int ChildNodeId)> Stems { get; set; } = new();
        // Node ids of the parent couple this branch hangs from. Lets
        // the client hide the branch lines when the parent bubbles are
        // collapsed.
        public List<int> ParentNodeIds { get; set; } = new();
    }
    public class SiblingLine
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y { get; set; }
    }

    private TreeLayout BuildLayout(List<FamilyTreeNode> nodes, List<FamilyRelationship> edges, string ownerUserId)
    {
        var layout = new TreeLayout();
        if (nodes.Count == 0) return layout;

        // Pair every node with their spouses (multi-valued — a node can
        // have multiple Spouse edges, e.g. remarriage). The FIRST spouse
        // forms the primary CoupleUnit; any further spouses are tracked
        // separately and rendered as adjacent bubbles with their own
        // marriage line.
        var spousesOf = nodes.ToDictionary(n => n.Id, _ => new List<int>());
        foreach (var e in edges.Where(x => x.RelType == FamilyRelationType.Spouse))
        {
            if (spousesOf.ContainsKey(e.FromNodeId) && !spousesOf[e.FromNodeId].Contains(e.ToNodeId))
                spousesOf[e.FromNodeId].Add(e.ToNodeId);
            if (spousesOf.ContainsKey(e.ToNodeId) && !spousesOf[e.ToNodeId].Contains(e.FromNodeId))
                spousesOf[e.ToNodeId].Add(e.FromNodeId);
        }

        var parentEdges = edges.Where(x => x.RelType == FamilyRelationType.Parent).ToList();
        // parents[child] = list of parent node ids
        var parents = nodes.ToDictionary(n => n.Id, _ => new List<int>());
        foreach (var e in parentEdges)
        {
            if (parents.TryGetValue(e.ToNodeId, out var list)) list.Add(e.FromNodeId);
        }
        // childrenOf[parent] = list of child node ids
        var childrenOf = nodes.ToDictionary(n => n.Id, _ => new HashSet<int>());
        foreach (var e in parentEdges)
        {
            if (childrenOf.TryGetValue(e.FromNodeId, out var set)) set.Add(e.ToNodeId);
        }

        // Local gender inference — male/female/unknown — used both to
        // order couples (father on the left, mother on the right) and
        // to recognise an implicit couple from two singletons that
        // share a child.
        bool IsMaleNode(FamilyTreeNode n)
        {
            if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
            {
                var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("m")) return true;
                if (g.StartsWith("f") || g.StartsWith("w")) return false;
            }
            if (n.NodeKind == FamilyNodeKind.Profile && n.TargetProfile != null)
            {
                var g = (n.TargetProfile.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("m")) return true;
                if (g.StartsWith("f") || g.StartsWith("w")) return false;
                var r = (n.TargetProfile.Relation ?? "").ToLowerInvariant();
                string[] male = { "father","dad","papa","papà","grandfather","grandpa","nonno","uncle","zio","brother","fratello","son","figlio","husband","marito","sposo","nephew" };
                foreach (var m in male) if (r.Contains(m)) return true;
            }
            return false;
        }
        bool IsFemaleNode(FamilyTreeNode n)
        {
            if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
            {
                var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("f") || g.StartsWith("w")) return true;
                if (g.StartsWith("m")) return false;
            }
            if (n.NodeKind == FamilyNodeKind.Profile && n.TargetProfile != null)
            {
                var g = (n.TargetProfile.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("f") || g.StartsWith("w")) return true;
                if (g.StartsWith("m")) return false;
                var r = (n.TargetProfile.Relation ?? "").ToLowerInvariant();
                string[] female = { "mother","mom","mum","mamma","grandmother","granny","grandma","nonna","aunt","zia","sister","sorella","daughter","figlia","wife","moglie","sposa","niece" };
                foreach (var f in female) if (r.Contains(f)) return true;
            }
            return false;
        }

        // Build couple units. Each unit's primary key is the lower of the
        // two node ids; we keep a map from each member node to its unit.
        // When the two have known genders, place the male on the left
        // and the female on the right so grandparents add cleanly above:
        // paternal grandparents drift further left, maternal further right.
        var nodeById = nodes.ToDictionary(n => n.Id);
        var unitOfNode = new Dictionary<int, CoupleUnit>();
        var allUnits = new List<CoupleUnit>();
        (FamilyTreeNode L, FamilyTreeNode R) OrderCouple(FamilyTreeNode a, FamilyTreeNode b)
        {
            var aM = IsMaleNode(a); var bM = IsMaleNode(b);
            var aF = IsFemaleNode(a); var bF = IsFemaleNode(b);
            if (aM && bF) return (a, b);
            if (aF && bM) return (b, a);
            // Fall back to deterministic id order.
            return a.Id < b.Id ? (a, b) : (b, a);
        }
        // additionalSpouses[centralNodeId] = list of partner node ids
        // that aren't in the central node's primary couple. Rendered as
        // separate bubbles adjacent to the primary couple.
        var additionalSpouses = new Dictionary<int, List<int>>();
        var consumedAsAdditional = new HashSet<int>();
        foreach (var n in nodes.OrderBy(x => x.Id))
        {
            if (unitOfNode.ContainsKey(n.Id)) continue;
            if (consumedAsAdditional.Contains(n.Id)) continue;
            // Eligible partners = spouses not already in a couple and not
            // already claimed as someone else's additional spouse.
            var partners = spousesOf[n.Id]
                .Where(pid => nodeById.ContainsKey(pid)
                              && !unitOfNode.ContainsKey(pid)
                              && !consumedAsAdditional.Contains(pid))
                .ToList();
            if (partners.Count == 0)
            {
                // Before stranding n as an orphan singleton, see if n
                // is a spouse of someone ALREADY paired — i.e. n is
                // the 2nd (or 3rd, …) husband/wife of a hub node. The
                // hub got claimed by its first partner earlier in the
                // loop, so partner-eligibility filtered out everyone
                // for n. Re-register n as that hub's additional spouse
                // instead, and the extra-spouse render block places n
                // adjacent to the hub with a marriage line.
                int? hubId = null;
                foreach (var pid in spousesOf[n.Id])
                {
                    if (!nodeById.ContainsKey(pid)) continue;
                    if (!unitOfNode.ContainsKey(pid)) continue;
                    hubId = pid;
                    break;
                }
                if (hubId.HasValue)
                {
                    if (!additionalSpouses.TryGetValue(hubId.Value, out var list))
                    {
                        list = new List<int>();
                        additionalSpouses[hubId.Value] = list;
                    }
                    if (!list.Contains(n.Id)) list.Add(n.Id);
                    consumedAsAdditional.Add(n.Id);
                }
                else
                {
                    var unit = new CoupleUnit { Left = n, Right = null };
                    unitOfNode[n.Id] = unit;
                    allUnits.Add(unit);
                }
            }
            else
            {
                var partner = nodeById[partners[0]];
                var (l, r) = OrderCouple(n, partner);
                var unit = new CoupleUnit { Left = l, Right = r };
                unitOfNode[unit.Left.Id]  = unit;
                unitOfNode[unit.Right!.Id] = unit;
                allUnits.Add(unit);
                // Any remaining partners become "additional spouses of n".
                for (int i = 1; i < partners.Count; i++)
                {
                    if (!additionalSpouses.TryGetValue(n.Id, out var list))
                    {
                        list = new List<int>();
                        additionalSpouses[n.Id] = list;
                    }
                    list.Add(partners[i]);
                    consumedAsAdditional.Add(partners[i]);
                }
            }
        }

        // Implicit-couple pass: any child whose parents are in two
        // distinct singleton units gets those parents merged into a
        // couple unit. Lets the layout draw a marriage line + a single
        // drop to the child even when the writer never explicitly
        // recorded a Spouse edge between the parents.
        foreach (var child in nodes)
        {
            var ps = parents.GetValueOrDefault(child.Id) ?? new();
            if (ps.Count < 2) continue;
            // Find two parents that are currently each in their own
            // singleton unit (Right == null) and not already paired.
            FamilyTreeNode? a = null;
            FamilyTreeNode? b = null;
            foreach (var pid in ps)
            {
                if (!nodeById.TryGetValue(pid, out var pn)) continue;
                if (!unitOfNode.TryGetValue(pid, out var pu)) continue;
                if (pu.Right != null) continue;          // already in a couple
                if (a == null) { a = pn; }
                else if (unitOfNode[a.Id] != pu) { b = pn; break; }
            }
            if (a == null || b == null) continue;
            var unitA = unitOfNode[a.Id];
            var unitB = unitOfNode[b.Id];
            if (unitA == unitB) continue;
            var (l2, r2) = OrderCouple(a, b);
            // Merge unitB into unitA, preserving father-left / mother-right.
            // Update unitOfNode for BOTH members — OrderCouple may flip them
            // so unitB's member could end up as l2 (left) rather than r2.
            unitA.Left = l2;
            unitA.Right = r2;
            unitOfNode[l2.Id] = unitA;
            unitOfNode[r2.Id] = unitA;
            allUnits.Remove(unitB);
        }

        // A couple's children = children of either spouse, deduped, then
        // mapped to the couple unit each child belongs to (their own
        // marriage, if any).
        foreach (var u in allUnits)
        {
            var childIds = new HashSet<int>();
            foreach (var c in childrenOf[u.Left.Id]) childIds.Add(c);
            if (u.Right != null) foreach (var c in childrenOf[u.Right.Id]) childIds.Add(c);
            foreach (var cid in childIds)
            {
                if (!unitOfNode.TryGetValue(cid, out var childUnit)) continue;
                if (childUnit.Parent == null)
                {
                    childUnit.Parent = u;
                    u.Children.Add(childUnit);
                }
            }
        }

        // Find the unit containing self. If self has no parents on the
        // tree, its unit (or an ancestor of it) is an anchor; walk up
        // from self to the topmost ancestor whose unit has no parent.
        var selfNode = nodes.FirstOrDefault(n =>
            n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == ownerUserId);
        layout.SelfNodeId = selfNode?.Id;
        // TryGetValue, not indexer — defensive against the (rare)
        // multi-spouse scenario where the main pairing pass would have
        // consumed self as someone else's additional spouse. In that
        // case selfUnit is null and centering falls through.
        CoupleUnit? selfUnit = null;
        if (selfNode != null) unitOfNode.TryGetValue(selfNode.Id, out selfUnit);

        CoupleUnit? rootForSelf = selfUnit;
        while (rootForSelf?.Parent != null) rootForSelf = rootForSelf.Parent;

        // Sort each couple's Children by the lineage-spouse's BirthYear
        // (oldest first → left). The lineage spouse is whichever spouse
        // of the child unit is actually a descendant of the parent unit
        // (the OTHER side is an in-law and has no birth-year signal
        // relative to this lineage). Children without a birth year
        // sort last, in their original order.
        int LineageBirthYear(CoupleUnit child, CoupleUnit parentUnit)
        {
            var pKids = childrenOf[parentUnit.Left.Id];
            int? targetId = null;
            if (pKids.Contains(child.Left.Id)) targetId = child.Left.Id;
            else if (child.Right != null && pKids.Contains(child.Right.Id)) targetId = child.Right.Id;
            if (!targetId.HasValue && parentUnit.Right != null)
            {
                var pRKids = childrenOf[parentUnit.Right.Id];
                if (pRKids.Contains(child.Left.Id)) targetId = child.Left.Id;
                else if (child.Right != null && pRKids.Contains(child.Right.Id)) targetId = child.Right.Id;
            }
            if (!targetId.HasValue) return int.MaxValue;
            if (!nodeById.TryGetValue(targetId.Value, out var node)) return int.MaxValue;
            int? year = node.NodeKind == FamilyNodeKind.Profile
                ? node.TargetProfile?.BirthYear
                : node.TargetUser?.BirthYear;
            return year ?? int.MaxValue;
        }
        foreach (var pu in allUnits)
        {
            if (pu.Children.Count <= 1) continue;
            pu.Children = pu.Children
                .Select((c, idx) => (c, year: LineageBirthYear(c, pu), idx))
                .OrderBy(t => t.year)
                .ThenBy(t => t.idx)
                .Select(t => t.c)
                .ToList();
        }

        // Reorder children of each unit on the "self path" (rootForSelf →
        // selfUnit) so that the child whose subtree contains self ends up
        // RIGHTMOST among its siblings. Concretely: Sylvia (Marco's
        // sibling) ends up to the left of Marco — and Marco's couple sits
        // at the right edge, leaving room past it for the in-law parents
        // anchor (Daniela's parents) to land cleanly to the right.
        if (rootForSelf != null && selfUnit != null)
        {
            var pathStack = new Stack<CoupleUnit>();
            bool FindPath(CoupleUnit current)
            {
                pathStack.Push(current);
                if (current == selfUnit) return true;
                foreach (var c in current.Children)
                    if (FindPath(c)) return true;
                pathStack.Pop();
                return false;
            }
            FindPath(rootForSelf);
            var selfPath = new HashSet<CoupleUnit>(pathStack);
            foreach (var u in selfPath)
            {
                if (u.Children.Count <= 1) continue;
                var onPath = u.Children.FirstOrDefault(c => selfPath.Contains(c));
                if (onPath != null && u.Children[^1] != onPath)
                {
                    u.Children.Remove(onPath);
                    u.Children.Add(onPath);
                }
            }
        }

        // Detect secondary-parent anchors. A unit qualifies when:
        //   - It has no Parent of its own (it's an anchor candidate)
        //   - It owns no Children in the descendant tree
        //   - At least one of its members is a parent of a node whose
        //     couple unit was claimed by some other unit
        // Only used by the top-down fallback layout (when no selfUnit).
        // The bottom-up layout below doesn't use these.
        var secondaryAnchors = new Dictionary<CoupleUnit, (FamilyTreeNode Target, CoupleUnit TargetUnit)>();
        if (selfUnit == null)
        {
            foreach (var u in allUnits.Where(x => x.Parent == null && x.Children.Count == 0))
            {
                var members = u.Right != null ? new[] { u.Left, u.Right } : new[] { u.Left };
                foreach (var m in members)
                {
                    foreach (var childId in childrenOf[m.Id])
                    {
                        if (!unitOfNode.TryGetValue(childId, out var childUnit)) continue;
                        if (childUnit.Parent != null && childUnit.Parent != u)
                        {
                            secondaryAnchors[u] = (nodeById[childId], childUnit);
                            break;
                        }
                    }
                    if (secondaryAnchors.ContainsKey(u)) break;
                }
            }
            foreach (var (sec, tgt) in secondaryAnchors)
            {
                tgt.TargetUnit.HasBothParents = true;
            }
        }

        // Compute SpouseCenterDist for each couple unit based on the
        // ancestor subtree above each spouse. Cascades bottom-up so a
        // couple with grandparents on both sides ends up wide enough
        // that the grandparent couples fit above each spouse without
        // overlapping at the midpoint, and that widening propagates
        // down so descendant rows have matching space.
        // AncGap is the minimum horizontal padding between two ancestor
        // subtrees that sit above adjacent spouses. ColGap/2 = 30 px is
        // tight enough that the tree packs in without the row-by-row
        // padding compounding into wide gaps several generations down.
        const double AncGap = ColGap / 2.0;

        // AncExt only matters for nodes whose ancestor subtree is
        // ACTUALLY centered ABOVE them in the bottom-up layout — that's
        // the members of selfUnit + recursively the members of each
        // ancestor unit reached by walking up via spouse-of-spouse
        // parents. Everyone else (siblings of ancestors, descendants
        // of self, in-laws of in-laws) shares a parent unit ABOVE
        // their row with siblings, so they don't need horizontal
        // accommodation for "their" ancestor subtree at their own
        // row. Without this scoping, Marco+Daniela's wide SCD
        // cascaded down to Sara+Jackson making them sit 500+ px apart.
        var ancestorChain = new HashSet<int>();
        if (selfUnit != null)
        {
            var visitedUnits = new HashSet<CoupleUnit>();
            var queue = new Queue<CoupleUnit>();
            queue.Enqueue(selfUnit);
            visitedUnits.Add(selfUnit);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                ancestorChain.Add(u.Left.Id);
                if (u.Right != null) ancestorChain.Add(u.Right.Id);
                var members = u.Right != null
                    ? new[] { u.Left.Id, u.Right.Id }
                    : new[] { u.Left.Id };
                foreach (var mid in members)
                {
                    var ps = parents.GetValueOrDefault(mid) ?? new();
                    if (ps.Count == 0) continue;
                    if (!unitOfNode.TryGetValue(ps[0], out var pUnit)) continue;
                    if (visitedUnits.Add(pUnit)) queue.Enqueue(pUnit);
                }
            }
        }

        var nodeAncExt = new Dictionary<int, (double L, double R)>();
        (double L, double R) ComputeAncExt(int nodeId)
        {
            if (nodeAncExt.TryGetValue(nodeId, out var cached)) return cached;
            // Scope: only nodes on the ancestor chain accumulate AncExt.
            // Off-chain nodes (siblings, descendants, distant in-laws)
            // don't have a "above-them-stacked" ancestor subtree, so
            // their lateral accommodation needs nothing extra.
            if (selfUnit != null && !ancestorChain.Contains(nodeId)) return (0.0, 0.0);
            nodeAncExt[nodeId] = (0.0, 0.0); // memo placeholder against cycles
            var ps = parents.GetValueOrDefault(nodeId) ?? new();
            if (ps.Count == 0) return (0.0, 0.0);
            var pUnit = unitOfNode.GetValueOrDefault(ps[0]);
            if (pUnit == null) return (0.0, 0.0);
            if (pUnit.Right == null)
            {
                var (parentL, parentR) = ComputeAncExt(pUnit.Left.Id);
                var rL = Math.Max(BubbleW / 2.0, parentL);
                var rR = Math.Max(BubbleW / 2.0, parentR);
                nodeAncExt[nodeId] = (rL, rR);
                return (rL, rR);
            }
            var (llL, llR) = ComputeAncExt(pUnit.Left.Id);
            var (rrL, rrR) = ComputeAncExt(pUnit.Right.Id);
            var lRightEdge = Math.Max(BubbleW / 2.0, llR);
            var rLeftEdge  = Math.Max(BubbleW / 2.0, rrL);
            var defaultDist = BubbleW + ColGap / 2.0;
            var scd = Math.Max(defaultDist, lRightEdge + rLeftEdge + AncGap);
            // Return the IMMEDIATE parent couple's half-width, not the
            // cascading great-grandparent extent. Deeper ancestor rows
            // accommodate themselves via each couple's own SCD (computed
            // recursively); descendants stay tight at the bottom rather
            // than inheriting every ancestor's spread. Without this,
            // Marco+Daniela had to sit ~495 px apart to fit four
            // generations of ancestry without overlap at any row.
            var halfWidth = scd / 2.0 + BubbleW / 2.0;
            nodeAncExt[nodeId] = (halfWidth, halfWidth);
            return (halfWidth, halfWidth);
        }
        // Every couple — self, parents, grandparents, all of them — sits
        // at the same tight spousal gap. The "wider couples to fit
        // ancestors above" model interleaved opposite-side branches and
        // looked nothing like a real pedigree. Instead, the OUTWARD-SHIFT
        // placement (PlaceAncestors below) and the per-row overlap
        // resolution carry all the spread. Each generation fans wider
        // horizontally by stacking tight couples further out — same
        // pattern as FamilySearch.
        foreach (var u in allUnits)
        {
            if (u.Right == null) { u.SpouseCenterDist = 0; continue; }
            u.SpouseCenterDist = BubbleW + ColGap / 2.0;
        }

        // Layout pass. Two modes:
        //  - Bottom-up (preferred): selfUnit at canvas centre, ancestors
        //    grow recursively above each spouse (each parent couple sits
        //    directly above its child-spouse with a clean vertical drop),
        //    descendants below via the existing cursor-based layout.
        //  - Top-down (fallback): when no selfUnit on the tree, use the
        //    old anchor-then-secondary approach.
        foreach (var u in allUnits) ComputeWidth(u);
        var placedUnits = new HashSet<CoupleUnit>();
        double totalWidth = BubbleW;

        if (selfUnit != null)
        {
            // BOTTOM-UP from selfUnit.
            void PlaceUnit(CoupleUnit u, double centerX, double topY)
            {
                u.NodePositions.Clear();
                if (u.Right == null)
                    u.NodePositions[u.Left.Id] = (centerX - BubbleW / 2.0, topY);
                else
                {
                    u.NodePositions[u.Left.Id]  = (centerX - u.SpouseCenterDist / 2.0 - BubbleW / 2.0, topY);
                    u.NodePositions[u.Right.Id] = (centerX + u.SpouseCenterDist / 2.0 - BubbleW / 2.0, topY);
                }
            }
            (double cX, double topY) UnitCenter(CoupleUnit u)
            {
                var lp = u.NodePositions[u.Left.Id];
                if (u.Right == null) return (lp.x + BubbleW / 2.0, lp.y);
                var rp = u.NodePositions[u.Right.Id];
                return ((lp.x + rp.x + BubbleW) / 2.0, lp.y);
            }
            // Descendants of u, cursor-based under u's midpoint. Siblings
            // are stacked SiblingGap apart (tighter than ColGap) so kids
            // read as a cohesive row instead of feeling spread out.
            void PlaceDescendants(CoupleUnit u)
            {
                if (u.Children.Count == 0) return;
                var (uCx, uY) = UnitCenter(u);
                double childTotal = 0;
                for (int i = 0; i < u.Children.Count; i++)
                {
                    childTotal += u.Children[i].SubtreeWidth;
                    if (i < u.Children.Count - 1) childTotal += SiblingGap;
                }
                double cur = uCx - childTotal / 2.0;
                for (int i = 0; i < u.Children.Count; i++)
                {
                    var c = u.Children[i];
                    if (!placedUnits.Contains(c))
                    {
                        PlaceUnit(c, cur + c.SubtreeWidth / 2.0, uY + RowH);
                        placedUnits.Add(c);
                        PlaceDescendants(c);
                    }
                    cur += c.SubtreeWidth + (i < u.Children.Count - 1 ? SiblingGap : 0);
                }
            }
            // Binary-recursive pedigree bisection. Each spouse of u owns
            // an ANCESTOR SLOT — a horizontal range in which all of that
            // spouse's ancestors are arranged. The parent couple sits
            // tight (default SCD) at the midpoint of the slot, and the
            // slot is bisected for the next generation: parent.Left
            // inherits the left half of the slot, parent.Right inherits
            // the right half. Slot width halves at each generation up, so
            // adjacent same-generation couples fan out wider and wider
            // toward the top of the tree — the standard FamilySearch /
            // ancestry-chart shape. Drops bend gracefully from each
            // parent couple's midpoint down to the descendant spouse,
            // which is laterally offset from the parent midpoint.
            int ComputeMaxAncestorDepth(int rootId)
            {
                int best = 0;
                var seen = new HashSet<int>();
                var q = new Queue<(int id, int depth)>();
                q.Enqueue((rootId, 0));
                while (q.Count > 0)
                {
                    var (cur, d) = q.Dequeue();
                    if (!seen.Add(cur)) continue;
                    if (d > best) best = d;
                    var ps = parents.GetValueOrDefault(cur) ?? new();
                    foreach (var pid in ps) q.Enqueue((pid, d + 1));
                }
                return best;
            }
            int maxGen = 0;
            maxGen = Math.Max(maxGen, ComputeMaxAncestorDepth(selfUnit.Left.Id));
            if (selfUnit.Right != null)
                maxGen = Math.Max(maxGen, ComputeMaxAncestorDepth(selfUnit.Right.Id));
            // BaseSlot = minimum slot width at the deepest generation.
            // A tight couple spans BubbleW + SpouseCenterDist (~190 px);
            // 280 leaves ~90 px of breathing room around each couple at
            // the top of the tree. The canvas doubles in half-width with
            // each additional generation, so a 3-gen tree fans across
            // ~2240 px and a 4-gen tree across ~4480 px.
            const double BaseSlot = 280.0;
            double canvasHalfWidth = BaseSlot * Math.Pow(2, Math.Max(0, maxGen - 1));

            void PlaceAncestors(CoupleUnit u, double slotLeft, double slotRight)
            {
                double mid = (slotLeft + slotRight) / 2.0;
                var spouseSlots = u.Right != null
                    ? new[] {
                        (sp: u.Left,  sLeft: slotLeft, sRight: mid),
                        (sp: u.Right, sLeft: mid,      sRight: slotRight)
                      }
                    : new[] {
                        (sp: u.Left,  sLeft: slotLeft, sRight: slotRight)
                      };
                foreach (var (sp, sLeft, sRight) in spouseSlots)
                {
                    var pIds = parents.GetValueOrDefault(sp.Id) ?? new();
                    if (pIds.Count == 0) continue;
                    var pUnit = unitOfNode.GetValueOrDefault(pIds[0]);
                    if (pUnit == null || placedUnits.Contains(pUnit)) continue;
                    var spPos = u.NodePositions[sp.Id];
                    double slotCenter = (sLeft + sRight) / 2.0;
                    PlaceUnit(pUnit, slotCenter, spPos.y - RowH);
                    placedUnits.Add(pUnit);
                    // Make sure the descendant unit u is in pUnit.Children
                    // so the ChildBranch emitter draws the drop from pUnit
                    // down to u. The original child-assignment loop only
                    // attached u to ONE parent unit (whichever iterated
                    // first); in bottom-up the OTHER side of the family
                    // needs its own drop too.
                    if (!pUnit.Children.Contains(u))
                        pUnit.Children.Add(u);
                    // Siblings of sp at u's row — stacked on the SAME
                    // SIDE as sp. Siblings of u.Left go to the left of
                    // u; siblings of u.Right (in-law siblings, like
                    // Daniela's brother Diego) go to the right.
                    bool spIsLeft = u.Right != null && sp.Id == u.Left.Id;
                    double anchorY = spPos.y;
                    // Use ColGap (not SiblingGap) here so each sibling-of-
                    // ancestor at the descendant row has visible breathing
                    // room — both from the lineage unit and from each
                    // other. SiblingGap is for kids stacked under the SAME
                    // parent couple; these siblings have their OWN potential
                    // children below them and need column-level spacing.
                    double cur = spIsLeft
                        ? u.NodePositions[u.Left.Id].x - ColGap
                        : u.NodePositions[u.Right!.Id].x + BubbleW + ColGap;
                    foreach (var sib in pUnit.Children)
                    {
                        bool spInSib = sib.Left.Id == sp.Id
                                    || (sib.Right != null && sib.Right.Id == sp.Id);
                        if (spInSib || placedUnits.Contains(sib)) continue;
                        double sibCenterX;
                        if (spIsLeft)
                        {
                            cur -= sib.SubtreeWidth;
                            sibCenterX = cur + sib.SubtreeWidth / 2.0;
                            cur -= ColGap;
                        }
                        else
                        {
                            sibCenterX = cur + sib.SubtreeWidth / 2.0;
                            cur += sib.SubtreeWidth + ColGap;
                        }
                        PlaceUnit(sib, sibCenterX, anchorY);
                        placedUnits.Add(sib);
                        PlaceDescendants(sib);
                    }
                    PlaceAncestors(pUnit, sLeft, sRight);
                }
            }

            PlaceUnit(selfUnit, 0, 0);
            placedUnits.Add(selfUnit);
            PlaceDescendants(selfUnit);
            PlaceAncestors(selfUnit, -canvasHalfWidth, +canvasHalfWidth);

            // Disconnected units (floating, no path to self) — place to
            // the right of everything currently positioned.
            double maxRight = 0;
            foreach (var u in placedUnits)
                foreach (var kv in u.NodePositions)
                    if (kv.Value.x + BubbleW > maxRight) maxRight = kv.Value.x + BubbleW;
            double floatCursor = maxRight + ColGap * 2;
            foreach (var u in allUnits)
            {
                if (placedUnits.Contains(u)) continue;
                if (u.Parent != null && placedUnits.Contains(u.Parent)) continue;
                PlaceUnit(u, floatCursor + u.SubtreeWidth / 2.0, 0);
                placedUnits.Add(u);
                PlaceDescendants(u);
                floatCursor += u.SubtreeWidth + ColGap;
            }
            totalWidth = floatCursor;
        }
        else
        {
            // TOP-DOWN fallback.
            var anchorUnits = allUnits
                .Where(u => u.Parent == null && !secondaryAnchors.ContainsKey(u))
                .ToList();
            double cursorX = 0;
            foreach (var u in anchorUnits)
            {
                var centerX = cursorX + u.SubtreeWidth / 2.0;
                Position(u, centerX, 0);
                placedUnits.Add(u);
                cursorX += u.SubtreeWidth + ColGap;
            }
            totalWidth = Math.Max(BubbleW, cursorX - ColGap);
            foreach (var (sec, tgt) in secondaryAnchors)
            {
                ComputeWidth(sec);
                var primary = tgt.TargetUnit.Parent;
                if (primary == null) continue;
                if (!tgt.TargetUnit.NodePositions.ContainsKey(tgt.Target.Id)) continue;
                var targetPos = tgt.TargetUnit.NodePositions[tgt.Target.Id];
                double targetCx = targetPos.x + BubbleW / 2.0;
                var primLeftPos = primary.NodePositions[primary.Left.Id];
                Position(sec, targetCx, primLeftPos.y);
            }
        }

        // Overlap resolution at each ancestor row above selfUnit. The
        // bottom-up layout keeps Marco+Daniela tight, which leaves
        // higher rows (grandparent, great-grandparent) potentially
        // overlapping — Marco's maternal grandparents bump into
        // Daniela's paternal grandparents at the centre line. Walk
        // each row left-to-right and push overlapping units (and
        // their ENTIRE ancestor chain going up) further right, until
        // the row sits cleanly with at least SiblingGap between
        // adjacent bubbles. The drops from a shifted ancestor to its
        // descendant bend naturally because the descendant stayed
        // put — the existing ChildBranch emitter handles bent stems.
        if (selfUnit != null)
        {
            void ShiftAncestorChain(CoupleUnit u, double dx)
            {
                var visited = new HashSet<CoupleUnit>();
                var stack = new Stack<CoupleUnit>();
                stack.Push(u);
                visited.Add(u);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    var positions = cur.NodePositions.ToList();
                    cur.NodePositions.Clear();
                    foreach (var kv in positions)
                        cur.NodePositions[kv.Key] = (kv.Value.x + dx, kv.Value.y);
                    var members = cur.Right != null
                        ? new[] { cur.Left.Id, cur.Right.Id }
                        : new[] { cur.Left.Id };
                    foreach (var mid in members)
                    {
                        var pIds = parents.GetValueOrDefault(mid) ?? new();
                        if (pIds.Count == 0) continue;
                        var pUnit = unitOfNode.GetValueOrDefault(pIds[0]);
                        if (pUnit == null || visited.Contains(pUnit)) continue;
                        visited.Add(pUnit);
                        stack.Push(pUnit);
                    }
                }
            }

            var selfY = selfUnit.NodePositions[selfUnit.Left.Id].y;
            // Collect (Y, nodeId, unit) for every positioned bubble
            // ABOVE selfUnit's row.
            var rowBuckets = new Dictionary<double, List<(int NodeId, CoupleUnit Unit)>>();
            foreach (var u in allUnits)
            {
                foreach (var kv in u.NodePositions)
                {
                    if (kv.Value.y >= selfY) continue; // descendants and self row stay tight
                    if (!rowBuckets.TryGetValue(kv.Value.y, out var list))
                    {
                        list = new List<(int, CoupleUnit)>();
                        rowBuckets[kv.Value.y] = list;
                    }
                    list.Add((kv.Key, u));
                }
            }
            // Process rows from closest-to-self going UP so each row's
            // shifts cascade into its own ancestors before we get to
            // them. Multiple passes per row in case a shift creates a
            // new overlap further right.
            //
            // Group bubbles BY their owning unit so the overlap check
            // works on couple bounding boxes — Mario+Christa stays
            // contiguous (Mario.x to Christa.x+BW), and we shift the
            // WHOLE couple right when its bbox starts before the
            // previous couple's bbox-right + SiblingGap. Without this
            // grouping, Christa (Marco's mom) could land between Egidio
            // and Liana (Daniela's parents) because individually the
            // adjacent bubbles passed the per-bubble check.
            foreach (var y in rowBuckets.Keys.OrderByDescending(yv => yv))
            {
                bool changed = true;
                int safety = 0;
                while (changed && safety++ < 32)
                {
                    changed = false;
                    var groups = new Dictionary<CoupleUnit, (double left, double right)>();
                    foreach (var (nodeId, unit) in rowBuckets[y])
                    {
                        if (!unit.NodePositions.TryGetValue(nodeId, out var pos)) continue;
                        var l = pos.x;
                        var r = pos.x + BubbleW;
                        if (groups.TryGetValue(unit, out var prev))
                            groups[unit] = (Math.Min(prev.left, l), Math.Max(prev.right, r));
                        else
                            groups[unit] = (l, r);
                    }
                    var sorted = groups.OrderBy(kv => kv.Value.left).ToList();
                    for (int i = 0; i + 1 < sorted.Count; i++)
                    {
                        var leftRight = sorted[i].Value.right;
                        var rightUnit = sorted[i + 1].Key;
                        var rightLeft = sorted[i + 1].Value.left;
                        var minRightLeft = leftRight + SiblingGap;
                        if (rightLeft + 0.5 < minRightLeft)
                        {
                            ShiftAncestorChain(rightUnit, minRightLeft - rightLeft);
                            changed = true;
                            break; // restart this row's scan
                        }
                    }
                }
            }

            // Descendant-row breathing sweep. The bisection above keeps
            // ancestors fanning wide, but at the descendant row siblings-
            // of-ancestors (Sylvia, Diego+Tammy, etc.) can still crowd
            // selfUnit — and crucially their SUBTREE (any kids they have
            // now or might add later) needs room too. So we compute each
            // row-mate's full subtree footprint (their bubbles plus every
            // descendant's bubbles) and push it AWAY from selfUnit until
            // there's at least ColGap between footprints. selfUnit itself
            // is the centre-of-canvas anchor and never moves.
            (double L, double R) SubtreeBox(CoupleUnit u)
            {
                double L = double.MaxValue, R = double.MinValue;
                var seen = new HashSet<CoupleUnit>();
                var stack = new Stack<CoupleUnit>();
                stack.Push(u);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!seen.Add(cur)) continue;
                    foreach (var kv in cur.NodePositions)
                    {
                        if (kv.Value.x < L) L = kv.Value.x;
                        if (kv.Value.x + BubbleW > R) R = kv.Value.x + BubbleW;
                    }
                    foreach (var c in cur.Children) stack.Push(c);
                }
                if (L > R) return (0, 0);
                return (L, R);
            }
            void ShiftUnitAndDescendants(CoupleUnit u, double dx)
            {
                var seen = new HashSet<CoupleUnit>();
                var stack = new Stack<CoupleUnit>();
                stack.Push(u);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!seen.Add(cur)) continue;
                    var positions = cur.NodePositions.ToList();
                    cur.NodePositions.Clear();
                    foreach (var kv in positions)
                        cur.NodePositions[kv.Key] = (kv.Value.x + dx, kv.Value.y);
                    foreach (var c in cur.Children) stack.Push(c);
                }
            }

            var rowMates = allUnits
                .Where(u => u != selfUnit
                         && u.NodePositions.Count > 0
                         && u.NodePositions.Values.Any(p => Math.Abs(p.y - selfY) < 0.5))
                .ToList();
            var selfBox = SubtreeBox(selfUnit);
            // Left side: items whose right edge sits left of selfUnit — sort
            // by right edge descending (closest first) and push left so
            // each item clears its right neighbour by ColGap.
            var leftMates = rowMates
                .Where(u => SubtreeBox(u).R <= selfBox.L + 1)
                .OrderByDescending(u => SubtreeBox(u).R)
                .ToList();
            double leftBoundary = selfBox.L;
            foreach (var u in leftMates)
            {
                var bb = SubtreeBox(u);
                double requiredRight = leftBoundary - ColGap;
                if (bb.R > requiredRight)
                {
                    ShiftUnitAndDescendants(u, requiredRight - bb.R);
                }
                leftBoundary = SubtreeBox(u).L;
            }
            // Right side: mirror — closest first, push right.
            var rightMates = rowMates
                .Where(u => SubtreeBox(u).L >= selfBox.R - 1)
                .OrderBy(u => SubtreeBox(u).L)
                .ToList();
            double rightBoundary = selfBox.R;
            foreach (var u in rightMates)
            {
                var bb = SubtreeBox(u);
                double requiredLeft = rightBoundary + ColGap;
                if (bb.L < requiredLeft)
                {
                    ShiftUnitAndDescendants(u, requiredLeft - bb.L);
                }
                rightBoundary = SubtreeBox(u).R;
            }
        }

        // Precompute additional-spouse positions (pre-shift) so the
        // minLeft check below accounts for them — without this, an
        // extra-spouse bubble that sits to the left of the central node
        // could land off the left edge of the canvas after shifting.
        var extraSpousePositions = new List<(int CentralId, int PartnerId, double X, double Y, bool ExtrasGoLeft)>();
        // Each additional spouse takes the bubble width + a generous
        // gap. Use 2 * (BubbleW + ColGap) per step so the label below
        // the bubble (up to ~96 px wide with our wrap rule) has room
        // to breathe — at the smaller 140-px step long names like
        // "Herbert Karl Heinz Kuerbis" still landed on top of the
        // next extra's label even though the bubbles themselves had
        // 60 px of clearance.
        const double ExtraSpouseStep = 2 * (BubbleW + ColGap);
        foreach (var (centralId, extras) in additionalSpouses)
        {
            if (!unitOfNode.TryGetValue(centralId, out var centralUnit)) continue;
            if (!centralUnit.NodePositions.TryGetValue(centralId, out var cPos)) continue;
            bool extrasGoLeft = centralUnit.Right != null && centralUnit.Left.Id == centralId;
            for (int i = 0; i < extras.Count; i++)
            {
                var pid = extras[i];
                if (!nodeById.ContainsKey(pid)) continue;
                double ex = extrasGoLeft
                    ? cPos.x - (i + 1) * ExtraSpouseStep
                    : cPos.x + BubbleW + ColGap + i * ExtraSpouseStep;
                extraSpousePositions.Add((centralId, pid, ex, cPos.y, extrasGoLeft));
            }
        }

        // Shift everything horizontally so self lands at canvas centre.
        double targetSelfX = Math.Max(600, totalWidth) / 2.0 - BubbleW / 2.0;
        double shiftX = 0;
        if (selfNode != null)
        {
            var selfPos = selfUnit!.NodePositions[selfNode.Id];
            shiftX = targetSelfX - selfPos.x;
        }
        // Make sure left edge stays >= padding.
        double minLeft = double.MaxValue;
        foreach (var u in allUnits)
        {
            foreach (var kv in u.NodePositions)
            {
                if (kv.Value.x + shiftX < minLeft) minLeft = kv.Value.x + shiftX;
            }
        }
        foreach (var ex in extraSpousePositions)
        {
            if (ex.X + shiftX < minLeft) minLeft = ex.X + shiftX;
        }
        if (minLeft < 40) shiftX += (40 - minLeft);

        // Emit positioned nodes + edges into the layout result.
        // Skip secondary-parent anchor units here — they're emitted by
        // the dedicated secondary-anchor block below (with their bent
        // connector line). Emitting them in both places duplicates their
        // node entries and breaks the view's positionedById dictionary.
        foreach (var u in allUnits)
        {
            if (secondaryAnchors.ContainsKey(u)) continue;
            foreach (var kv in u.NodePositions)
            {
                if (!nodeById.TryGetValue(kv.Key, out var node)) continue;
                layout.Nodes.Add(new PositionedNode { Node = node, X = kv.Value.x + shiftX, Y = kv.Value.y });
            }
            // Marriage line — bottom-edge of the two bubbles, sitting
            // along the row's centre so children can drop from it.
            if (u.Right != null)
            {
                var lpos = u.NodePositions[u.Left.Id];
                var rpos = u.NodePositions[u.Right.Id];
                layout.Marriages.Add(new MarriageLine
                {
                    X1 = lpos.x + BubbleW + shiftX,
                    X2 = rpos.x + shiftX,
                    Y  = lpos.y + BubbleH / 2.0,
                    LeftNodeId = u.Left.Id,
                    RightNodeId = u.Right.Id
                });
            }
            // Children: build the drop + branch + per-child stems.
            if (u.Children.Count > 0)
            {
                var lpos = u.NodePositions[u.Left.Id];
                double coupleMidX = u.Right != null
                    ? (lpos.x + BubbleW / 2.0 + u.NodePositions[u.Right.Id].x + BubbleW / 2.0) / 2.0
                    : lpos.x + BubbleW / 2.0;
                double dropTopY = lpos.y + BubbleH;
                double childrenTopY = lpos.y + RowH;
                double branchY = (dropTopY + childrenTopY) / 2.0;

                var branch = new ChildBranch
                {
                    DropX  = coupleMidX + shiftX,
                    DropY1 = u.Right != null ? lpos.y + BubbleH / 2.0 : dropTopY,
                    DropY2 = branchY,
                    BranchY = branchY,
                    Stems = new List<(double, double, double, int)>(),
                    ParentNodeIds = u.Right != null
                        ? new List<int> { u.Left.Id, u.Right.Id }
                        : new List<int> { u.Left.Id }
                };
                double minStemX = double.MaxValue, maxStemX = double.MinValue;
                foreach (var child in u.Children)
                {
                    // The stem lands on the SPECIFIC spouse who is actually
                    // a child of u — not blindly on the unit's Left. So if
                    // Will+Erna parent Christa (the Right of Mario+Christa),
                    // the drop line ends on Christa rather than on Mario.
                    int linkSpouseId = child.Left.Id;
                    bool leftIsChild  = childrenOf[u.Left.Id].Contains(child.Left.Id)
                                     || (u.Right != null && childrenOf[u.Right.Id].Contains(child.Left.Id));
                    bool rightIsChild = child.Right != null
                                     && (childrenOf[u.Left.Id].Contains(child.Right.Id)
                                         || (u.Right != null && childrenOf[u.Right.Id].Contains(child.Right.Id)));
                    if (!leftIsChild && rightIsChild) linkSpouseId = child.Right!.Id;
                    var linkPos = child.NodePositions[linkSpouseId];
                    double stemX = linkPos.x + BubbleW / 2.0 + shiftX;
                    double stemY1 = branchY;
                    double stemY2 = linkPos.y;
                    branch.Stems.Add((stemX, stemY1, stemY2, linkSpouseId));
                    if (stemX < minStemX) minStemX = stemX;
                    if (stemX > maxStemX) maxStemX = stemX;
                }
                branch.BranchX1 = Math.Min(minStemX, branch.DropX);
                branch.BranchX2 = Math.Max(maxStemX, branch.DropX);
                layout.ChildBranches.Add(branch);
            }
        }

        // Explicit Sibling edges are no longer drawn — siblings are only
        // shown via shared parents. Keeping the data on disk so older
        // additions aren't lost, but visually they're invisible until
        // the user adds the common parent that makes the relation real.

        // Emit secondary-parent anchor units + their bent connector lines
        // down to the relevant spouse. These live outside the primary
        // descendant tree, so we add them after the main allUnits loop.
        foreach (var (sec, tgt) in secondaryAnchors)
        {
            if (!sec.NodePositions.ContainsKey(sec.Left.Id)) continue;
            foreach (var kv in sec.NodePositions)
            {
                if (!nodeById.TryGetValue(kv.Key, out var node)) continue;
                layout.Nodes.Add(new PositionedNode { Node = node, X = kv.Value.x + shiftX, Y = kv.Value.y });
            }
            if (sec.Right != null)
            {
                var lp = sec.NodePositions[sec.Left.Id];
                var rp = sec.NodePositions[sec.Right.Id];
                layout.Marriages.Add(new MarriageLine
                {
                    X1 = lp.x + BubbleW + shiftX,
                    X2 = rp.x + shiftX,
                    Y  = lp.y + BubbleH / 2.0,
                    LeftNodeId = sec.Left.Id,
                    RightNodeId = sec.Right.Id
                });
            }
            // Bent connector down to the child node (the spouse this
            // unit actually parents). FromX is the couple's midpoint —
            // marriage-line midpoint for a couple, bubble centre for a
            // singleton — and ToX/ToY is the child's top-centre.
            var secLp = sec.NodePositions[sec.Left.Id];
            double midX = sec.Right != null
                ? (secLp.x + BubbleW / 2.0 + sec.NodePositions[sec.Right.Id].x + BubbleW / 2.0) / 2.0
                : secLp.x + BubbleW / 2.0;
            double bottomY = sec.Right != null ? secLp.y + BubbleH / 2.0 : secLp.y + BubbleH;
            if (!tgt.TargetUnit.NodePositions.ContainsKey(tgt.Target.Id)) continue;
            var tp = tgt.TargetUnit.NodePositions[tgt.Target.Id];
            layout.SecondaryParents.Add(new SecondaryParentLink
            {
                FromX = midX + shiftX,
                FromY = bottomY,
                ToX = tp.x + BubbleW / 2.0 + shiftX,
                ToY = tp.y
            });
        }

        // Additional spouses — for any node with a Spouse edge to more
        // than one person, render each "extra" spouse as a bubble
        // adjacent to the central node, with its own marriage line. The
        // marriage line picks up the "+ Add child" button automatically
        // via MarriageLine.LeftNodeId / RightNodeId.
        // Children attribution still happens through the primary couple
        // for now — a future iteration can split by which spouse the
        // child was added with.
        foreach (var ex in extraSpousePositions)
        {
            if (!nodeById.TryGetValue(ex.PartnerId, out var partnerNode)) continue;
            if (!unitOfNode.TryGetValue(ex.CentralId, out var centralUnit)) continue;
            if (!centralUnit.NodePositions.TryGetValue(ex.CentralId, out var cPos)) continue;
            layout.Nodes.Add(new PositionedNode
            {
                Node = partnerNode,
                X = ex.X + shiftX,
                Y = ex.Y
            });
            double mY = ex.Y + BubbleH / 2.0;
            double mX1, mX2;
            int leftId, rightId;
            if (ex.ExtrasGoLeft)
            {
                mX1 = ex.X + BubbleW + shiftX;
                mX2 = cPos.x + shiftX;
                leftId = ex.PartnerId;
                rightId = ex.CentralId;
            }
            else
            {
                mX1 = cPos.x + BubbleW + shiftX;
                mX2 = ex.X + shiftX;
                leftId = ex.CentralId;
                rightId = ex.PartnerId;
            }
            layout.Marriages.Add(new MarriageLine
            {
                X1 = mX1, X2 = mX2, Y = mY,
                LeftNodeId = leftId, RightNodeId = rightId,
                IsAdditional = true
            });
        }

        // "+" placeholders for missing key relations (Father, Mother,
        // Spouse, Sibling, Child for self + Father/Mother of each
        // existing parent). Computed against the laid-out positions
        // above so they appear in the right slots.
        layout.Placeholders = ComputePlaceholders(nodes, edges, layout, selfNode);

        // Some placeholders sit ABOVE the topmost anchor (parents and
        // grandparents of self) and would land at negative Y. Shift the
        // whole layout — real bubbles + edge lines + placeholders — down
        // so the topmost element clears 40 px of padding.
        var topY = double.MaxValue;
        foreach (var p in layout.Nodes) if (p.Y < topY) topY = p.Y;
        foreach (var ph in layout.Placeholders) if (ph.Y < topY) topY = ph.Y;
        var yShift = topY < 40 ? (40 - topY) : 0;
        if (yShift > 0)
        {
            foreach (var p in layout.Nodes)     p.Y += yShift;
            foreach (var ph in layout.Placeholders) ph.Y += yShift;
            foreach (var m in layout.Marriages) m.Y += yShift;
            foreach (var b in layout.ChildBranches)
            {
                b.DropY1 += yShift; b.DropY2 += yShift; b.BranchY += yShift;
                for (int i = 0; i < b.Stems.Count; i++)
                    b.Stems[i] = (b.Stems[i].X, b.Stems[i].Y1 + yShift, b.Stems[i].Y2 + yShift, b.Stems[i].ChildNodeId);
            }
            foreach (var s in layout.Siblings)  s.Y += yShift;
            foreach (var sp in layout.SecondaryParents) { sp.FromY += yShift; sp.ToY += yShift; }
        }

        // Canvas size — generous padding around the laid-out bbox AND
        // any placeholders so a "+ Sibling" slot at the far left isn't
        // clipped.
        var maxX = 0.0; var maxY = 0.0;
        foreach (var p in layout.Nodes)     { if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; }
        foreach (var ph in layout.Placeholders) { if (ph.X > maxX) maxX = ph.X; if (ph.Y > maxY) maxY = ph.Y; }
        foreach (var sp in layout.SecondaryParents)
        {
            if (sp.FromX > maxX) maxX = sp.FromX;
            if (sp.ToX   > maxX) maxX = sp.ToX;
            if (sp.FromY > maxY) maxY = sp.FromY;
            if (sp.ToY   > maxY) maxY = sp.ToY;
        }
        layout.CanvasWidth  = layout.Nodes.Count == 0 ? 800 : maxX + BubbleW + 60;
        layout.CanvasHeight = layout.Nodes.Count == 0 ? 600 : maxY + BubbleH + 80;
        if (layout.CanvasWidth  < 800) layout.CanvasWidth = 800;
        if (layout.CanvasHeight < 400) layout.CanvasHeight = 400;
        return layout;
    }

    /// <summary>"+" placeholder bubbles for each missing key relation:
    /// father, mother, spouse, sibling, child for self, plus father /
    /// mother of any existing parent of self. Each placeholder carries
    /// the anchor + relationKind + kindLabel it represents so the JS can
    /// open the popup pre-configured when the user clicks it.</summary>
    private List<TreePlaceholder> ComputePlaceholders(
        List<FamilyTreeNode> nodes,
        List<FamilyRelationship> edges,
        TreeLayout layout,
        FamilyTreeNode? selfNode)
    {
        var result = new List<TreePlaceholder>();
        if (selfNode == null) return result;

        var selfPos = layout.Nodes.FirstOrDefault(p => p.Node.Id == selfNode.Id);
        if (selfPos == null) return result;

        // Build helper maps from the edges so we can ask "does this
        // person have a father / mother / spouse on the tree?"
        var parents = nodes.ToDictionary(n => n.Id, _ => new List<FamilyTreeNode>());
        var spouses = nodes.ToDictionary(n => n.Id, _ => new List<FamilyTreeNode>());
        var children = nodes.ToDictionary(n => n.Id, _ => new List<FamilyTreeNode>());
        var nodeById = nodes.ToDictionary(n => n.Id);
        foreach (var e in edges)
        {
            if (e.RelType == FamilyRelationType.Parent)
            {
                if (parents.ContainsKey(e.ToNodeId) && nodeById.TryGetValue(e.FromNodeId, out var p))
                    parents[e.ToNodeId].Add(p);
                if (children.ContainsKey(e.FromNodeId) && nodeById.TryGetValue(e.ToNodeId, out var c))
                    children[e.FromNodeId].Add(c);
            }
            else if (e.RelType == FamilyRelationType.Spouse)
            {
                if (spouses.ContainsKey(e.FromNodeId) && nodeById.TryGetValue(e.ToNodeId, out var s1))
                    spouses[e.FromNodeId].Add(s1);
                if (spouses.ContainsKey(e.ToNodeId) && nodeById.TryGetValue(e.FromNodeId, out var s2))
                    spouses[e.ToNodeId].Add(s2);
            }
        }

        string LabelForNode(FamilyTreeNode n) =>
            n.NodeKind == FamilyNodeKind.Profile
                ? (n.TargetProfile?.Nickname ?? n.TargetProfile?.DisplayName ?? "?")
                : (n.TargetUser?.Nickname ?? n.TargetUser?.DisplayName ?? n.TargetUser?.UserName ?? "?");

        // Cheap gender inference — prefers the explicit Gender field
        // on PersonProfile / ApplicationUser, falls back to scanning
        // the Relation hint when Gender isn't set.
        bool IsFemale(FamilyTreeNode n)
        {
            if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
            {
                var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("f") || g.StartsWith("w")) return true;
                if (g.StartsWith("m")) return false;
            }
            if (n.NodeKind == FamilyNodeKind.Profile && n.TargetProfile != null)
            {
                var g = (n.TargetProfile.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("f") || g.StartsWith("w")) return true;
                if (g.StartsWith("m")) return false;
            }
            var hint = (n.TargetProfile?.Relation ?? "").ToLowerInvariant();
            string[] femaleHints = { "mother","mom","mum","mamma","grandmother","granny","grandma","nonna",
                "aunt","zia","sister","sorella","daughter","figlia","wife","moglie","sposa","niece" };
            foreach (var h in femaleHints) if (hint.Contains(h)) return true;
            return false;
        }
        bool IsMale(FamilyTreeNode n)
        {
            if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
            {
                var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("m")) return true;
                if (g.StartsWith("f") || g.StartsWith("w")) return false;
            }
            if (n.NodeKind == FamilyNodeKind.Profile && n.TargetProfile != null)
            {
                var g = (n.TargetProfile.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("m")) return true;
                if (g.StartsWith("f") || g.StartsWith("w")) return false;
            }
            var hint = (n.TargetProfile?.Relation ?? "").ToLowerInvariant();
            string[] maleHints = { "father","dad","papa","papà","grandfather","grandpa","nonno",
                "uncle","zio","brother","fratello","son","figlio","husband","marito","sposo","nephew" };
            foreach (var h in maleHints) if (hint.Contains(h)) return true;
            return false;
        }

        var selfName = LabelForNode(selfNode);

        // Spouse placeholder — show only when self has no spouse yet.
        if (spouses[selfNode.Id].Count == 0)
        {
            result.Add(new TreePlaceholder
            {
                X = selfPos.X + ColW, Y = selfPos.Y,
                Label = "Spouse", Icon = "💍",
                AnchorNodeId = selfNode.Id, AnchorName = selfName,
                RelationKind = "Spouse", KindLabel = "Spouse"
            });
        }

        // Sibling placeholder — always offer one as an invitation.
        result.Add(new TreePlaceholder
        {
            X = selfPos.X - ColW, Y = selfPos.Y,
            Label = "Sibling", Icon = "👥",
            AnchorNodeId = selfNode.Id, AnchorName = selfName,
            RelationKind = "Sibling", KindLabel = "Sibling"
        });

        // Child placeholder — only when no children yet (otherwise the
        // existing children render normally and clutter is the worse evil).
        if (children[selfNode.Id].Count == 0)
        {
            result.Add(new TreePlaceholder
            {
                X = selfPos.X, Y = selfPos.Y + RowH,
                Label = "Child", Icon = "👶",
                AnchorNodeId = selfNode.Id, AnchorName = selfName,
                RelationKind = "Child", KindLabel = "Child"
            });
        }

        // Parent placeholders — Father + Mother above self if none yet,
        // or the missing one adjacent to the existing parent.
        var selfParents = parents[selfNode.Id];
        if (selfParents.Count == 0)
        {
            result.Add(new TreePlaceholder
            {
                X = selfPos.X - BubbleW - ColGap / 4.0, Y = selfPos.Y - RowH,
                Label = "Father", Icon = "👨",
                AnchorNodeId = selfNode.Id, AnchorName = selfName,
                RelationKind = "Parent", KindLabel = "Father"
            });
            result.Add(new TreePlaceholder
            {
                X = selfPos.X + BubbleW + ColGap / 4.0, Y = selfPos.Y - RowH,
                Label = "Mother", Icon = "👩",
                AnchorNodeId = selfNode.Id, AnchorName = selfName,
                RelationKind = "Parent", KindLabel = "Mother"
            });
        }
        else if (selfParents.Count == 1)
        {
            var existing = selfParents[0];
            var existingPos = layout.Nodes.FirstOrDefault(p => p.Node.Id == existing.Id);
            if (existingPos != null)
            {
                // If we can't tell the existing one's gender, default to
                // assuming father — the missing slot becomes "Mother". That's
                // a reasonable bias since most missing-second-parent cases
                // are mothers (in oral history terms).
                var existingIsFemale = IsFemale(existing);
                var missingIsFather = existingIsFemale;
                var missingLabel = missingIsFather ? "Father" : "Mother";
                var missingIcon  = missingIsFather ? "👨" : "👩";
                var newX = missingIsFather ? existingPos.X - ColW : existingPos.X + ColW;
                result.Add(new TreePlaceholder
                {
                    X = newX, Y = existingPos.Y,
                    Label = missingLabel, Icon = missingIcon,
                    AnchorNodeId = selfNode.Id, AnchorName = selfName,
                    RelationKind = "Parent", KindLabel = missingLabel
                });
            }
        }

        // Grandparent placeholders — for each existing parent of self,
        // add Father/Mother placeholders above them (one generation up).
        foreach (var parent in selfParents)
        {
            var parentPos = layout.Nodes.FirstOrDefault(p => p.Node.Id == parent.Id);
            if (parentPos == null) continue;
            var parentName = LabelForNode(parent);
            var parentParents = parents[parent.Id];
            if (parentParents.Count == 0)
            {
                result.Add(new TreePlaceholder
                {
                    X = parentPos.X - BubbleW - ColGap / 4.0, Y = parentPos.Y - RowH,
                    Label = "Father", Icon = "👨",
                    AnchorNodeId = parent.Id, AnchorName = parentName,
                    RelationKind = "Parent", KindLabel = "Father"
                });
                result.Add(new TreePlaceholder
                {
                    X = parentPos.X + BubbleW + ColGap / 4.0, Y = parentPos.Y - RowH,
                    Label = "Mother", Icon = "👩",
                    AnchorNodeId = parent.Id, AnchorName = parentName,
                    RelationKind = "Parent", KindLabel = "Mother"
                });
            }
            else if (parentParents.Count == 1)
            {
                var existing = parentParents[0];
                var existingPos = layout.Nodes.FirstOrDefault(p => p.Node.Id == existing.Id);
                if (existingPos != null)
                {
                    var existingIsFemale = IsFemale(existing);
                    var missingIsFather = existingIsFemale;
                    var missingLabel = missingIsFather ? "Father" : "Mother";
                    var missingIcon  = missingIsFather ? "👨" : "👩";
                    var newX = missingIsFather ? existingPos.X - ColW : existingPos.X + ColW;
                    result.Add(new TreePlaceholder
                    {
                        X = newX, Y = existingPos.Y,
                        Label = missingLabel, Icon = missingIcon,
                        AnchorNodeId = parent.Id, AnchorName = parentName,
                        RelationKind = "Parent", KindLabel = missingLabel
                    });
                }
            }
        }

        return result;
    }

    private class CoupleUnit
    {
        public FamilyTreeNode Left { get; set; } = null!;
        public FamilyTreeNode? Right { get; set; }
        public CoupleUnit? Parent { get; set; }
        public List<CoupleUnit> Children { get; set; } = new();
        public double SubtreeWidth { get; set; }
        public Dictionary<int, (double x, double y)> NodePositions { get; set; } = new();
        // True when this couple has BOTH sets of parents on the tree —
        // one above each spouse. Diagnostic only; SpouseCenterDist now
        // carries the actual widening.
        public bool HasBothParents { get; set; }
        // Center-to-center horizontal distance between Left and Right
        // spouses. Defaults to BubbleW + ColGap/2 (~110). Widened when
        // the ancestor subtrees above each spouse need space to fit
        // their own grandparents without overlapping at the midpoint.
        // Computed bottom-up from each spouse's AncestorExtent.
        public double SpouseCenterDist { get; set; }
    }

    private void ComputeWidth(CoupleUnit u)
    {
        // SubtreeWidth is the bounding-box width of the unit + everything
        // recursively below it, WITHOUT trailing padding — the caller adds
        // SiblingGap when stacking siblings horizontally.
        double ownW = u.Right != null
            ? u.SpouseCenterDist + BubbleW
            : BubbleW;
        if (u.Children.Count == 0)
        {
            u.SubtreeWidth = ownW;
            return;
        }
        double childrenW = 0;
        for (int i = 0; i < u.Children.Count; i++)
        {
            var c = u.Children[i];
            ComputeWidth(c);
            childrenW += c.SubtreeWidth;
            if (i < u.Children.Count - 1) childrenW += SiblingGap;
        }
        u.SubtreeWidth = Math.Max(ownW, childrenW);
    }

    private void Position(CoupleUnit u, double centerX, double topY)
    {
        u.NodePositions.Clear();
        if (u.Right != null)
        {
            u.NodePositions[u.Left.Id]  = (centerX - u.SpouseCenterDist / 2.0 - BubbleW / 2.0, topY);
            u.NodePositions[u.Right.Id] = (centerX + u.SpouseCenterDist / 2.0 - BubbleW / 2.0, topY);
        }
        else
        {
            u.NodePositions[u.Left.Id] = (centerX - BubbleW / 2.0, topY);
        }

        if (u.Children.Count == 0) return;
        double childrenTotal = 0;
        for (int i = 0; i < u.Children.Count; i++)
        {
            childrenTotal += u.Children[i].SubtreeWidth;
            if (i < u.Children.Count - 1) childrenTotal += SiblingGap;
        }
        double cursorX = centerX - childrenTotal / 2.0;
        for (int i = 0; i < u.Children.Count; i++)
        {
            var c = u.Children[i];
            Position(c, cursorX + c.SubtreeWidth / 2.0, topY + RowH);
            cursorX += c.SubtreeWidth + (i < u.Children.Count - 1 ? SiblingGap : 0);
        }
    }
}
