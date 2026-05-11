using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
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

        // Pickers: friends not yet on the tree + profiles not yet on the tree.
        var friendList = await _friends.GetFriendListAsync(userId);
        var onTreeUserIds = nodes
            .Where(n => n.NodeKind == FamilyNodeKind.Member && !string.IsNullOrEmpty(n.TargetUserId))
            .Select(n => n.TargetUserId!).ToHashSet();
        var onTreeProfileIds = nodes
            .Where(n => n.NodeKind == FamilyNodeKind.Profile && n.TargetProfileId.HasValue)
            .Select(n => n.TargetProfileId!.Value).ToHashSet();
        ViewBag.AvailableMembers = friendList.Friends
            .Where(f => !onTreeUserIds.Contains(f.User.Id))
            .Select(f => f.User)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .ToList();
        var allProfiles = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync();
        ViewBag.AvailableProfiles = allProfiles.Where(p => !onTreeProfileIds.Contains(p.Id)).ToList();
        ViewBag.CanMutate = await _premium.IsAvailableAsync(user, PremiumFeature.FamilyTree);
        ViewBag.Self = user;
        ViewBag.Layout = layout;

        return View(nodes);
    }

    // ── Add a member (existing Kronoscript user) ────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(string targetUserId, int relationToNodeId, AddRelation relationKind)
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

        await CreateRelationshipAsync(userId, node, relationToNodeId, relationKind);
        return RedirectToAction(nameof(Index));
    }

    // ── Add an existing People Profile ──────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProfile(int profileId, int relationToNodeId, AddRelation relationKind)
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

        await CreateRelationshipAsync(userId, node, relationToNodeId, relationKind);
        return RedirectToAction(nameof(Index));
    }

    // ── Create a new People Profile inline AND add to tree ──────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProfileAndAdd(
        string displayName,
        string? relation,
        int? birthYear,
        int? deathYear,
        int relationToNodeId,
        AddRelation relationKind)
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
            Relation = string.IsNullOrWhiteSpace(relation) ? null : relation.Trim(),
            BirthYear = birthYear,
            DeathYear = deathYear,
            Visibility = PostVisibility.Family,
            CreatedAt = DateTime.UtcNow
        };
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

        await CreateRelationshipAsync(userId, node, relationToNodeId, relationKind);
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

    private async Task CreateRelationshipAsync(string userId, FamilyTreeNode newNode, int relationToNodeId, AddRelation kind)
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
                break;
            case AddRelation.Child:
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
                // If the anchor has a spouse on the tree, the new child
                // becomes a child of the couple — second Parent edge.
                var spouseId = await GetSpouseNodeIdAsync(userId, anchor.Id);
                if (spouseId.HasValue
                    && !await EdgeExistsAsync(userId, spouseId.Value, newNode.Id, FamilyRelationType.Parent))
                {
                    _db.FamilyRelationships.Add(new FamilyRelationship
                    {
                        OwnerUserId = userId,
                        FromNodeId = spouseId.Value,
                        ToNodeId = newNode.Id,
                        RelType = FamilyRelationType.Parent
                    });
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
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public int? SelfNodeId { get; set; }
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

        // Explicit Sibling edges that didn't get converted into a shared-parent
        // setup get drawn as a thin dotted arc.
        foreach (var e in edges.Where(x => x.RelType == FamilyRelationType.Sibling))
        {
            var aPos = layout.Nodes.FirstOrDefault(p => p.Node.Id == e.FromNodeId);
            var bPos = layout.Nodes.FirstOrDefault(p => p.Node.Id == e.ToNodeId);
            if (aPos == null || bPos == null) continue;
            layout.Siblings.Add(new SiblingLine
            {
                X1 = aPos.X + BubbleW / 2.0,
                X2 = bPos.X + BubbleW / 2.0,
                Y  = Math.Min(aPos.Y, bPos.Y) - 24
            });
        }

        // Canvas size — generous padding around the laid-out bbox.
        layout.CanvasWidth  = layout.Nodes.Count == 0 ? 800
            : layout.Nodes.Max(n => n.X) + BubbleW + 60;
        layout.CanvasHeight = layout.Nodes.Count == 0 ? 600
            : layout.Nodes.Max(n => n.Y) + BubbleH + 80;
        if (layout.CanvasWidth  < 800) layout.CanvasWidth = 800;
        if (layout.CanvasHeight < 400) layout.CanvasHeight = 400;
        return layout;
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
