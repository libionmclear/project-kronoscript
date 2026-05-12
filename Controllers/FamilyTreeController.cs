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
        int? secondParentNodeId = null)
    {
        if (!await GateAsync()) return Forbid();
        var userId = _userManager.GetUserId(User)!;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }

        var profile = new PersonProfile
        {
            CreatorUserId = userId,
            DisplayName = displayName.Trim(),
            Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim(),
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
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public int? SelfNodeId { get; set; }
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
        public List<(double X, double Y1, double Y2)> Stems { get; set; } = new();
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

        // Pair every node with their spouse (if any). Each spouse edge
        // creates a couple unit; un-paired nodes become singleton units.
        var spouseOf = new Dictionary<int, int>();
        foreach (var e in edges.Where(x => x.RelType == FamilyRelationType.Spouse))
        {
            spouseOf[e.FromNodeId] = e.ToNodeId;
            spouseOf[e.ToNodeId]   = e.FromNodeId;
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

        // Build couple units. Each unit's primary key is the lower of the
        // two node ids; we keep a map from each member node to its unit.
        var nodeById = nodes.ToDictionary(n => n.Id);
        var unitOfNode = new Dictionary<int, CoupleUnit>();
        var allUnits = new List<CoupleUnit>();
        foreach (var n in nodes.OrderBy(x => x.Id))
        {
            if (unitOfNode.ContainsKey(n.Id)) continue;
            if (spouseOf.TryGetValue(n.Id, out var partnerId) && nodeById.ContainsKey(partnerId))
            {
                var partner = nodeById[partnerId];
                var unit = new CoupleUnit
                {
                    Left  = n.Id < partner.Id ? n : partner,
                    Right = n.Id < partner.Id ? partner : n
                };
                unitOfNode[unit.Left.Id]  = unit;
                unitOfNode[unit.Right!.Id] = unit;
                allUnits.Add(unit);
            }
            else
            {
                var unit = new CoupleUnit { Left = n, Right = null };
                unitOfNode[n.Id] = unit;
                allUnits.Add(unit);
            }
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
        CoupleUnit? selfUnit = selfNode != null ? unitOfNode[selfNode.Id] : null;

        CoupleUnit? rootForSelf = selfUnit;
        while (rootForSelf?.Parent != null) rootForSelf = rootForSelf.Parent;

        // Layout: width pass + position pass on each anchor (root) unit.
        var anchorUnits = allUnits.Where(u => u.Parent == null).ToList();
        foreach (var u in anchorUnits) ComputeWidth(u);

        // Place anchors horizontally one after another so disconnected
        // families don't overlap.
        double cursorX = 0;
        foreach (var u in anchorUnits)
        {
            var centerX = cursorX + u.SubtreeWidth / 2.0;
            Position(u, centerX, 0);
            cursorX += u.SubtreeWidth + ColGap;
        }
        var totalWidth = Math.Max(BubbleW, cursorX - ColGap);

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
        if (minLeft < 40) shiftX += (40 - minLeft);

        // Emit positioned nodes + edges into the layout result.
        foreach (var u in allUnits)
        {
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
                    Y  = lpos.y + BubbleH / 2.0
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
                    Stems = new List<(double, double, double)>()
                };
                double minStemX = double.MaxValue, maxStemX = double.MinValue;
                foreach (var child in u.Children)
                {
                    // A child unit's "anchor X" for stem is the midpoint of the unit
                    // (between the spouses if any, else just over the child bubble).
                    var leftPos = child.NodePositions[child.Left.Id];
                    double anchorX = child.Right != null
                        ? (leftPos.x + BubbleW / 2.0 + child.NodePositions[child.Right.Id].x + BubbleW / 2.0) / 2.0
                        : leftPos.x + BubbleW / 2.0;
                    // The stem actually lands on the child bubble closest to the parent —
                    // the left member of the unit — so the line doesn't cross the marriage
                    // line of the child's own couple.
                    double stemX = leftPos.x + BubbleW / 2.0 + shiftX;
                    double stemY1 = branchY;
                    double stemY2 = leftPos.y;
                    branch.Stems.Add((stemX, stemY1, stemY2));
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
                    b.Stems[i] = (b.Stems[i].X, b.Stems[i].Y1 + yShift, b.Stems[i].Y2 + yShift);
            }
            foreach (var s in layout.Siblings)  s.Y += yShift;
        }

        // Canvas size — generous padding around the laid-out bbox AND
        // any placeholders so a "+ Sibling" slot at the far left isn't
        // clipped.
        var maxX = 0.0; var maxY = 0.0;
        foreach (var p in layout.Nodes)     { if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; }
        foreach (var ph in layout.Placeholders) { if (ph.X > maxX) maxX = ph.X; if (ph.Y > maxY) maxY = ph.Y; }
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

        // Cheap gender inference — re-uses the same hints the
        // RelationshipCalculator uses (Relation field on profiles,
        // Gender on members).
        bool IsFemale(FamilyTreeNode n)
        {
            if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
            {
                var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
                if (g.StartsWith("f") || g.StartsWith("w")) return true;
                return false;
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
                return g.StartsWith("m");
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
    }

    private void ComputeWidth(CoupleUnit u)
    {
        double ownW = u.Right != null ? (2 * BubbleW + ColGap / 2.0) : BubbleW;
        if (u.Children.Count == 0)
        {
            u.SubtreeWidth = ownW + ColGap;
            return;
        }
        double childrenW = 0;
        foreach (var c in u.Children) { ComputeWidth(c); childrenW += c.SubtreeWidth; }
        u.SubtreeWidth = Math.Max(ownW + ColGap, childrenW);
    }

    private void Position(CoupleUnit u, double centerX, double topY)
    {
        u.NodePositions.Clear();
        if (u.Right != null)
        {
            // Two bubbles centred around centerX with the gap between them.
            double leftX  = centerX - BubbleW - ColGap / 4.0;
            double rightX = centerX + ColGap / 4.0;
            u.NodePositions[u.Left.Id]  = (leftX, topY);
            u.NodePositions[u.Right.Id] = (rightX, topY);
        }
        else
        {
            u.NodePositions[u.Left.Id] = (centerX - BubbleW / 2.0, topY);
        }

        if (u.Children.Count == 0) return;
        double childrenTotal = 0;
        foreach (var c in u.Children) childrenTotal += c.SubtreeWidth;
        double cursorX = centerX - childrenTotal / 2.0;
        foreach (var c in u.Children)
        {
            Position(c, cursorX + c.SubtreeWidth / 2.0, topY + RowH);
            cursorX += c.SubtreeWidth;
        }
    }
}
