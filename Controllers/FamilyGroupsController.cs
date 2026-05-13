using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// CRUD + membership + post-curation for Family Groups — ad-hoc
/// overlapping family memberships ("Descendants of great-grandfather X",
/// "My kids + wife only"). Premium Family-tier feature: creating and
/// managing a group requires premium; adding an EXISTING story to a
/// group requires the adder to have premium too. Members without
/// premium are read-mostly — they comment, react, and tag themselves
/// only. Co-admin promotion targets must have premium.
/// </summary>
[Authorize]
public class FamilyGroupsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPremiumService _premium;
    private readonly IFriendService _friends;
    private readonly INotificationService _notifications;

    public FamilyGroupsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IPremiumService premium,
        IFriendService friends,
        INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _premium = premium;
        _friends = friends;
        _notifications = notifications;
    }

    /// <summary>Send the same notification to every member of a group
    /// except the actor (the one who triggered the event). Used by
    /// add-post, add-member, role-change actions so the rest of the
    /// group sees activity in their bell badge.</summary>
    private async Task NotifyGroupAsync(
        int groupId, string actorUserId, string excludeUserId,
        NotificationType type, string text, string linkUrl)
    {
        var memberIds = await _db.FamilyGroupMembers
            .Where(m => m.FamilyGroupId == groupId && m.UserId != excludeUserId)
            .Select(m => m.UserId)
            .ToListAsync();
        foreach (var uid in memberIds)
        {
            await _notifications.CreateAsync(uid, type, text, linkUrl, actorUserId);
        }
    }

    // GET: /FamilyGroups — list every group the current user is a member
    // or admin of. Entry-point gate: if the feature is in Off mode for
    // this viewer (admins always pass via IPremiumService), bounce home
    // rather than render an empty page.
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);

        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
        {
            TempData["Info"] = "Family Groups aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var memberships = await _db.FamilyGroupMembers
            .Include(m => m.FamilyGroup)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Role)            // admin first, co-admin, then member
            .ThenBy(m => m.FamilyGroup!.Name)
            .ToListAsync();

        // Member counts in one query so the index doesn't N+1.
        var groupIds = memberships.Select(m => m.FamilyGroupId).ToList();
        var counts = await _db.FamilyGroupMembers
            .Where(m => groupIds.Contains(m.FamilyGroupId))
            .GroupBy(m => m.FamilyGroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        ViewBag.MemberCounts = counts;
        ViewBag.CanCreate    = await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups);
        return View(memberships);
    }

    // GET: /FamilyGroups/Create
    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
        {
            TempData["Error"] = "Creating Family Groups requires a premium subscription.";
            return RedirectToAction(nameof(Index));
        }
        return View(new FamilyGroup());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FamilyGroup model)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
        {
            return Forbid();
        }

        // The form doesn't post CreatorUserId — we set it ourselves.
        // ASP.NET's NRT-driven Required validation would otherwise
        // refuse every submit, silently re-rendering an empty form.
        ModelState.Remove(nameof(model.CreatorUserId));
        ModelState.Remove(nameof(model.Creator));

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A name is required.");
        }

        if (!ModelState.IsValid) return View(model);

        model.CreatorUserId = userId;
        model.CreatedAt     = DateTime.UtcNow;
        model.UpdatedAt     = null;
        model.Description   = string.IsNullOrWhiteSpace(model.Description)
            ? null
            : model.Description.Trim();
        model.Name = model.Name.Trim();

        _db.FamilyGroups.Add(model);
        await _db.SaveChangesAsync();

        // Creator is the founding Admin. Persist the membership row
        // explicitly so role checks across the controller can hit a
        // single table without special-casing the creator.
        _db.FamilyGroupMembers.Add(new FamilyGroupMember
        {
            FamilyGroupId = model.Id,
            UserId        = userId,
            Role          = FamilyGroupRole.Admin
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Group \"{model.Name}\" created.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // GET: /FamilyGroups/Feed/5 — dedicated feed view for the group.
    // Same posts the Details page shows, but rendered with the proper
    // _FeedCard partial (likes, reactions, photo carousel, comments
    // surface) instead of the compact list-group rows. Members and
    // admins both land here when they click "Feed" from the group home.
    public async Task<IActionResult> Feed(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
        {
            TempData["Info"] = "Family Groups aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var group = await _db.FamilyGroups
            .Include(g => g.Creator)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        var myMembership = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == id && m.UserId == userId);
        if (myMembership == null && !User.IsInRole("Admin")) return Forbid();

        var groupPosts = await _db.FamilyGroupPosts
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Owner)
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Media)
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Likes)
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Comments)
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Channel)
            .Include(p => p.AddedBy)
            .Where(p => p.FamilyGroupId == id)
            .OrderByDescending(p => p.AddedAt)
            .ToListAsync();

        // Build FeedPostViewModel list so _FeedCard renders the post the
        // same way it does on the personal feed.
        var feedItems = groupPosts
            .Where(gp => gp.LifeEventPost != null && gp.LifeEventPost.DeletedAt == null)
            .Select(gp => new MyStoryTold.Models.ViewModels.FeedPostViewModel
            {
                Post = gp.LifeEventPost!,
                LikeCount = gp.LifeEventPost!.Likes?.Count ?? 0,
                CurrentUserLiked = gp.LifeEventPost.Likes?.Any(l => l.UserId == userId) ?? false,
                CurrentUserReaction = gp.LifeEventPost.Likes?.FirstOrDefault(l => l.UserId == userId)?.ReactionType
            })
            .ToList();

        ViewBag.Group     = group;
        ViewBag.CanManage = myMembership?.Role == FamilyGroupRole.Admin
                         || myMembership?.Role == FamilyGroupRole.CoAdmin;
        return View(feedItems);
    }

    // GET: /FamilyGroups/Details/5 — group home: members list + the
    // story feed (posts attached to this group). Only members can view.
    public async Task<IActionResult> Details(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);

        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
        {
            TempData["Info"] = "Family Groups aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var group = await _db.FamilyGroups
            .Include(g => g.Creator)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        var myMembership = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == id && m.UserId == userId);
        if (myMembership == null && !User.IsInRole("Admin")) return Forbid();

        var members = await _db.FamilyGroupMembers
            .Include(m => m.User)
            .Where(m => m.FamilyGroupId == id)
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.User!.FirstName)
            .ToListAsync();

        var posts = await _db.FamilyGroupPosts
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Owner)
            .Include(p => p.LifeEventPost).ThenInclude(lp => lp!.Media)
            .Include(p => p.AddedBy)
            .Where(p => p.FamilyGroupId == id)
            .OrderByDescending(p => p.AddedAt)
            .ToListAsync();

        ViewBag.Group       = group;
        ViewBag.Members     = members;
        ViewBag.Posts       = posts;
        ViewBag.MyRole      = myMembership?.Role ?? FamilyGroupRole.Member;
        ViewBag.IsAdmin     = myMembership?.Role == FamilyGroupRole.Admin;
        ViewBag.IsCoAdmin   = myMembership?.Role == FamilyGroupRole.CoAdmin;
        ViewBag.CanManage   = ViewBag.IsAdmin || ViewBag.IsCoAdmin;
        // Members without premium can comment/react/tag-self only. The
        // "Add story to this group" surface is gated by their own
        // premium status — not the group's.
        ViewBag.CanAddPosts = await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups);
        return View(group);
    }

    // GET: /FamilyGroups/Edit/5 — admin/co-admin can rename + edit
    // description. Members can't see this page.
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        var myMembership = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == id && m.UserId == userId);
        bool canManage = myMembership?.Role == FamilyGroupRole.Admin
                      || myMembership?.Role == FamilyGroupRole.CoAdmin;
        if (!canManage && !User.IsInRole("Admin")) return Forbid();

        // Editing requires premium even for co-admins (they were promoted
        // because they have premium; if it lapsed they shouldn't be
        // editing). Members never reach here.
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
        {
            TempData["Error"] = "Editing this group requires an active premium subscription.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(group);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FamilyGroup model)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        var myMembership = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == id && m.UserId == userId);
        bool canManage = myMembership?.Role == FamilyGroupRole.Admin
                      || myMembership?.Role == FamilyGroupRole.CoAdmin;
        if (!canManage && !User.IsInRole("Admin")) return Forbid();

        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
            return Forbid();

        ModelState.Remove(nameof(model.CreatorUserId));
        ModelState.Remove(nameof(model.Creator));
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A name is required.");
        }
        if (!ModelState.IsValid)
        {
            // Re-render with the persisted CreatorUserId so the form
            // round-trips cleanly.
            model.CreatorUserId = group.CreatorUserId;
            return View(model);
        }

        group.Name        = model.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(model.Description)
            ? null
            : model.Description.Trim();
        group.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Group updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /FamilyGroups/Delete/5 — admin only. Cascades to members,
    // post-links, and any future child rows via FK config.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        // Only the founding admin (or app admin) can delete the whole
        // group — co-admins can manage day-to-day but not dissolve it.
        if (group.CreatorUserId != userId && !User.IsInRole("Admin")) return Forbid();

        _db.FamilyGroups.Remove(group);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Group \"{group.Name}\" deleted.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /FamilyGroups/AddMember
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(int groupId, string targetUserId)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);

        if (string.IsNullOrEmpty(targetUserId)) return BadRequest();

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return NotFound();

        var myMembership = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == userId);
        bool canManage = myMembership?.Role == FamilyGroupRole.Admin
                      || myMembership?.Role == FamilyGroupRole.CoAdmin;
        if (!canManage) return Forbid();

        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
            return Forbid();

        // Only friends can be added — same restriction as FamilyTree
        // member adds. Avoids stranger-spam invites.
        var fl = await _friends.GetFriendListAsync(userId);
        if (!fl.Friends.Any(f => f.User.Id == targetUserId))
        {
            TempData["Error"] = "You can only add friends to a group.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        // Already a member? No-op — let the redirect carry the success
        // message rather than throwing on the unique-index collision.
        var existing = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == targetUserId);
        if (existing != null)
        {
            TempData["Info"] = "That person is already in the group.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        _db.FamilyGroupMembers.Add(new FamilyGroupMember
        {
            FamilyGroupId = groupId,
            UserId        = targetUserId,
            Role          = FamilyGroupRole.Member
        });
        await _db.SaveChangesAsync();

        // Notify the new member they were added, AND tell every other
        // member of the group there's a new face in the room.
        var groupLink = Url.Action(nameof(Details), new { id = groupId }) ?? "/";
        var actorName = (await _userManager.FindByIdAsync(userId))?.DisplayName ?? "Someone";
        var addedName = (await _userManager.FindByIdAsync(targetUserId))?.DisplayName ?? "a new member";
        await _notifications.CreateAsync(targetUserId, NotificationType.FamilyGroupMemberJoined,
            $"{actorName} added you to \"{group.Name}\".", groupLink, userId);
        await NotifyGroupAsync(groupId, userId, excludeUserId: targetUserId,
            NotificationType.FamilyGroupMemberJoined,
            $"{addedName} joined \"{group.Name}\".", groupLink);

        TempData["Success"] = "Member added.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }

    // POST: /FamilyGroups/RemoveMember
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int groupId, string targetUserId)
    {
        var userId = _userManager.GetUserId(User)!;

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return NotFound();

        var myMembership = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == userId);
        bool canManage = myMembership?.Role == FamilyGroupRole.Admin
                      || myMembership?.Role == FamilyGroupRole.CoAdmin;
        if (!canManage) return Forbid();

        // The founding admin can't be kicked — they have to dissolve the
        // group instead. Co-admins can remove members, but only the
        // founding admin can demote co-admins (handled in a separate
        // action). Block both edges here defensively.
        if (targetUserId == group.CreatorUserId)
        {
            TempData["Error"] = "The group's founder can't be removed. Delete the group instead.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        var target = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == targetUserId);
        if (target == null) return NotFound();

        // Co-admins can remove members but NOT other co-admins.
        if (target.Role == FamilyGroupRole.CoAdmin && myMembership!.Role != FamilyGroupRole.Admin)
        {
            return Forbid();
        }

        _db.FamilyGroupMembers.Remove(target);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Member removed.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }

    // POST: /FamilyGroups/Leave/5 — self-removal. Members and co-admins
    // can leave; the founding admin must use Delete instead (the group
    // can't be unfounded).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave(int id)
    {
        var userId = _userManager.GetUserId(User)!;

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();
        if (group.CreatorUserId == userId)
        {
            TempData["Error"] = "You founded this group — you must delete it rather than leave.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var me = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == id && m.UserId == userId);
        if (me == null) return RedirectToAction(nameof(Index));

        _db.FamilyGroupMembers.Remove(me);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"You left \"{group.Name}\".";
        return RedirectToAction(nameof(Index));
    }

    // POST: /FamilyGroups/PromoteCoAdmin — admin only. Target must have
    // premium (a co-admin who's not premium can't actually perform the
    // co-admin actions; we block at promotion-time rather than
    // half-promoting).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteCoAdmin(int groupId, string targetUserId)
    {
        var userId = _userManager.GetUserId(User)!;

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return NotFound();
        if (group.CreatorUserId != userId) return Forbid();

        var target = await _db.FamilyGroupMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == targetUserId);
        if (target == null) return NotFound();
        if (target.Role == FamilyGroupRole.Admin) return BadRequest();

        if (!await _premium.IsAvailableAsync(target.User, PremiumFeature.FamilyGroups))
        {
            TempData["Error"] = "Co-admins need an active premium subscription. Ask them to upgrade first.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        target.Role = FamilyGroupRole.CoAdmin;
        await _db.SaveChangesAsync();
        var groupLink = Url.Action(nameof(Details), new { id = groupId }) ?? "/";
        await _notifications.CreateAsync(targetUserId, NotificationType.FamilyGroupRoleChanged,
            $"You're now a co-admin of \"{group.Name}\".", groupLink, userId);
        TempData["Success"] = "Promoted to co-admin.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }

    // POST: /FamilyGroups/DemoteCoAdmin — admin only.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteCoAdmin(int groupId, string targetUserId)
    {
        var userId = _userManager.GetUserId(User)!;

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return NotFound();
        if (group.CreatorUserId != userId) return Forbid();

        var target = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == targetUserId);
        if (target == null) return NotFound();
        if (target.Role != FamilyGroupRole.CoAdmin) return BadRequest();

        target.Role = FamilyGroupRole.Member;
        await _db.SaveChangesAsync();
        var groupLink = Url.Action(nameof(Details), new { id = groupId }) ?? "/";
        await _notifications.CreateAsync(targetUserId, NotificationType.FamilyGroupRoleChanged,
            $"You're now a regular member of \"{group.Name}\".", groupLink, userId);
        TempData["Success"] = "Demoted to member.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }

    // POST: /FamilyGroups/AddPost — attach an existing post to this
    // group. Requires premium AND that the adder is the author of the
    // post (you can only contribute YOUR stories to a group; nobody
    // else can pin your story without your consent).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPost(int groupId, int postId)
    {
        var userId = _userManager.GetUserId(User)!;
        var user   = await _userManager.GetUserAsync(User);

        if (!await _premium.IsAvailableAsync(user, PremiumFeature.FamilyGroups))
            return Forbid();

        var me = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == userId);
        if (me == null) return Forbid();

        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null) return NotFound();
        if (post.OwnerUserId != userId) return Forbid();

        var existing = await _db.FamilyGroupPosts
            .FirstOrDefaultAsync(p => p.FamilyGroupId == groupId && p.LifeEventPostId == postId);
        if (existing != null)
        {
            TempData["Info"] = "That story is already in the group.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        _db.FamilyGroupPosts.Add(new FamilyGroupPost
        {
            FamilyGroupId   = groupId,
            LifeEventPostId = postId,
            AddedByUserId   = userId
        });
        await _db.SaveChangesAsync();

        // Tell everyone else in the group there's something new to read.
        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group != null)
        {
            var actorName = (await _userManager.FindByIdAsync(userId))?.DisplayName ?? "Someone";
            var title = string.IsNullOrWhiteSpace(post.Title)
                ? "a story"
                : $"\"{(post.Title.Length > 60 ? post.Title[..60] + "…" : post.Title)}\"";
            var feedLink = Url.Action(nameof(Feed), new { id = groupId }) ?? "/";
            await NotifyGroupAsync(groupId, userId, excludeUserId: userId,
                NotificationType.FamilyGroupPostAdded,
                $"{actorName} added {title} to \"{group.Name}\".", feedLink);
        }

        TempData["Success"] = "Story added to group.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }

    // POST: /FamilyGroups/RemovePost — detach a post from this group.
    // Author of the post OR admin/co-admin of the group can do this.
    // (Author can always pull their own story out of any group;
    // admins curate THEIR group.)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePost(int groupId, int postId)
    {
        var userId = _userManager.GetUserId(User)!;

        var link = await _db.FamilyGroupPosts
            .Include(p => p.LifeEventPost)
            .FirstOrDefaultAsync(p => p.FamilyGroupId == groupId && p.LifeEventPostId == postId);
        if (link == null) return NotFound();

        bool isAuthor = link.LifeEventPost?.OwnerUserId == userId;

        var me = await _db.FamilyGroupMembers
            .FirstOrDefaultAsync(m => m.FamilyGroupId == groupId && m.UserId == userId);
        bool canManage = me?.Role == FamilyGroupRole.Admin
                      || me?.Role == FamilyGroupRole.CoAdmin;

        if (!isAuthor && !canManage) return Forbid();

        _db.FamilyGroupPosts.Remove(link);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Story removed from group.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }
}
