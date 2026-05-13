using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Helpers;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class PostsController : Controller
{
    private readonly IPostService _postService;
    private readonly IPermissionService _permissionService;
    private readonly IDiffService _diffService;
    private readonly IFriendService _friendService;
    private readonly ITranslationService _translation;
    private readonly INotificationService _notifications;
    private readonly IFileStorageService _files;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public PostsController(
        IPostService postService,
        IPermissionService permissionService,
        IDiffService diffService,
        IFriendService friendService,
        ITranslationService translation,
        INotificationService notifications,
        IFileStorageService files,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IWebHostEnvironment env)
    {
        _postService = postService;
        _permissionService = permissionService;
        _diffService = diffService;
        _friendService = friendService;
        _translation = translation;
        _notifications = notifications;
        _files = files;
        _userManager = userManager;
        _db = db;
        _env = env;
    }

    /// <summary>Taggable people-profiles a user can drop into stories,
    /// photos, or the tag widget. Combines:
    ///   (a) profiles the user created themselves
    ///   (b) profiles created by family-tier connections, where the
    ///       profile's visibility is anything except Private
    /// Family is the trust boundary — siblings and parents can use each
    /// other's NPC cards without re-creating them. Friend-tier sharing
    /// still goes through a separate request flow (not yet built).</summary>
    private async Task<List<TaggableProfileViewModel>> LoadTaggableProfilesAsync(string userId)
    {
        var friendList = await _friendService.GetFriendListAsync(userId);
        var familyIds = friendList.Friends
            .Where(f => f.Tier == FriendTier.Family)
            .Select(f => f.User.Id)
            .ToList();

        var profileRows = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId
                        || (familyIds.Contains(p.CreatorUserId)
                            && p.Visibility != PostVisibility.Private))
            .OrderBy(p => p.DisplayName)
            .ToListAsync();

        return profileRows.Select(p => new TaggableProfileViewModel
        {
            ProfileId = p.Id,
            DisplayName = p.DisplayName,
            Relation = p.Relation,
            AvatarUrl = p.AvatarUrl
        }).ToList();
    }

    /// <summary>Photo-tag rows for every PostMedia in a post, grouped by mediaId.</summary>
    private async Task<Dictionary<int, List<MediaPersonTag>>> LoadMediaTagsForPostAsync(int postId)
    {
        var mediaIds = await _db.PostMedia
            .Where(m => m.PostId == postId)
            .Select(m => m.Id)
            .ToListAsync();
        if (mediaIds.Count == 0) return new();
        var tags = await _db.MediaPersonTags
            .Where(t => mediaIds.Contains(t.PostMediaId))
            .Include(t => t.TargetUser)
            .Include(t => t.TargetProfile)
            .ToListAsync();
        return tags.GroupBy(t => t.PostMediaId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Fan out notifications for a freshly added comment: post owner gets a Comment
    /// notification, parent-comment author gets a Reply (if it's a reply), and each
    /// @mentioned user gets a Mention. Self-actions are filtered inside the service.
    /// </summary>
    private async Task FanoutCommentNotificationsAsync(LifeEventPost post, Comment comment, string actorUserId)
    {
        var actor = await _userManager.FindByIdAsync(actorUserId);
        var actorName = actor?.DisplayName ?? actor?.UserName ?? "Someone";
        var titleSnippet = !string.IsNullOrWhiteSpace(post.Title)
            ? $"\"{post.Title}\""
            : "your story";
        var link = $"/Posts/Detail/{post.Id}";

        // Post owner
        if (post.OwnerUserId != actorUserId)
        {
            await _notifications.CreateAsync(
                post.OwnerUserId,
                NotificationType.Comment,
                $"{actorName} commented on {titleSnippet}",
                link,
                actorUserId);
        }

        // Reply → parent comment author
        if (comment.ParentCommentId.HasValue)
        {
            var parent = await _db.Comments.FindAsync(comment.ParentCommentId.Value);
            if (parent != null && parent.AuthorUserId != actorUserId && parent.AuthorUserId != post.OwnerUserId)
            {
                await _notifications.CreateAsync(
                    parent.AuthorUserId,
                    NotificationType.Reply,
                    $"{actorName} replied to your comment",
                    link,
                    actorUserId);
            }
        }

        // @mentions
        if (!string.IsNullOrWhiteSpace(comment.MentionedUserIds))
        {
            foreach (var mid in comment.MentionedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = mid.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == actorUserId) continue;
                await _notifications.CreateAsync(
                    trimmed,
                    NotificationType.Mention,
                    $"{actorName} mentioned you in a comment on {titleSnippet}",
                    link,
                    actorUserId);
            }
        }
    }

    // GET: /Posts/Timeline/{userId}?sort=created|event
    [HttpGet]
    public async Task<IActionResult> Timeline(string id, string sort = "created", string zoom = "year")
    {
        ViewBag.Zoom = zoom;
        var profileUser = await _userManager.FindByIdAsync(id);
        if (profileUser == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var isOwner = currentUserId == id;

        // Non-connected viewers are allowed in — visibility filter limits them to Public posts only
        var tier = isOwner ? FriendTier.Family : await _permissionService.GetViewerTierAsync(currentUserId, id);
        var posts = await _postService.GetTimelinePostsAsync(id, sort, tier, isOwner);

        // Batch-load all referenced people profiles in one query rather
        // than one round-trip per tag per post. PersonProfile rows are
        // shared by their creator, so the same profile id may appear
        // across many posts on a single timeline.
        var allProfileIds = new HashSet<int>();
        foreach (var p in posts)
        {
            if (string.IsNullOrEmpty(p.TaggedProfileIds)) continue;
            foreach (var pid in p.TaggedProfileIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(pid.Trim(), out var n)) allProfileIds.Add(n);
        }
        var profilesById = allProfileIds.Count == 0
            ? new Dictionary<int, PersonProfile>()
            : await _db.PersonProfiles
                .Where(p => allProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

        var postCards = new List<PostCardViewModel>();
        foreach (var post in posts)
        {
            string? diffHtml = null;
            if (post.Versions.Count >= 2)
            {
                var versions = post.Versions.OrderByDescending(v => v.VersionNumber).ToList();
                diffHtml = _diffService.ComputeDiffHtml(versions[1].BodySnapshot, versions[0].BodySnapshot);
            }

            var taggedUsers = new List<TaggedUserViewModel>();
            if (!string.IsNullOrEmpty(post.TaggedUserIds))
            {
                foreach (var tagId in post.TaggedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var taggedUser = await _userManager.FindByIdAsync(tagId.Trim());
                    if (taggedUser != null)
                        taggedUsers.Add(new TaggedUserViewModel
                        {
                            UserId = taggedUser.Id,
                            UserName = taggedUser.UserName!,
                            DisplayName = taggedUser.DisplayName ?? taggedUser.UserName!
                        });
                }
            }

            var taggedProfiles = new List<TaggedProfileViewModel>();
            if (!string.IsNullOrEmpty(post.TaggedProfileIds))
            {
                foreach (var pid in post.TaggedProfileIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(pid.Trim(), out var n) && profilesById.TryGetValue(n, out var pp))
                    {
                        taggedProfiles.Add(new TaggedProfileViewModel
                        {
                            ProfileId = pp.Id,
                            DisplayName = pp.DisplayName,
                            AvatarUrl = pp.AvatarUrl,
                            LinkedUserId = pp.LinkedUserId
                        });
                    }
                }
            }

            postCards.Add(new PostCardViewModel
            {
                Post = post,
                DiffHtml = diffHtml,
                LikeCount = post.Likes.Count,
                CurrentUserLiked = post.Likes.Any(l => l.UserId == currentUserId),
                CurrentUserReaction = post.Likes.FirstOrDefault(l => l.UserId == currentUserId)?.ReactionType,
                TaggedUsers = taggedUsers,
                TaggedProfiles = taggedProfiles
            });
        }

        // Build taggable friends list for post creation
        var friendList = await _friendService.GetFriendListAsync(currentUserId);
        var taggable = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        var vm = new TimelineViewModel
        {
            ProfileUser = profileUser,
            Posts = postCards,
            IsOwner = isOwner,
            ViewerTier = tier,
            CanComment = isOwner || profileUser.IsBiographical || tier is FriendTier.Friend or FriendTier.Family,
            CanReorder = isOwner || tier == FriendTier.Family,
            SortBy = sort,
            TaggableFriends = taggable
        };

        return View(vm);
    }

    // GET: /Posts/Create — supports ?postAsUserId= and ?channelId= deep
    // links so an admin clicking "+ New story" from a bio profile or a
    // channel page lands with that target pre-selected in the picker.
    // Also supports ?memoryOfId= for the "Connect your memory to this
    // story" link on a channel article: the new post starts with the
    // source article's date / location pre-filled and links back to it.
    [HttpGet]
    public async Task<IActionResult> Create(string? postAsUserId = null, int? channelId = null, int? memoryOfId = null,
                                            int? year = null, string? title = null, int? attachToGroupId = null)
    {
        var userId = _userManager.GetUserId(User)!;
        var friendList = await _friendService.GetFriendListAsync(userId);
        // attachToGroupId is the "crowdsource a memory" path: open the
        // composer pre-filled to write your version of someone else's
        // story, and on publish auto-attach the new post to that group.
        // The POST handler (Create model) reads the same hidden field
        // out of the form to do the attach in a single transaction.
        if (attachToGroupId.HasValue)
        {
            var canSee = await _db.FamilyGroupMembers
                .AnyAsync(m => m.FamilyGroupId == attachToGroupId.Value && m.UserId == userId);
            if (!canSee) attachToGroupId = null;
        }
        ViewBag.AttachToGroupId = attachToGroupId;
        ViewBag.TaggableFriends = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        // People profiles created by this user are eligible to be
        // tagged the same way real members are. The tag widget reads
        // ViewBag.TaggableProfiles alongside TaggableFriends. Family-tier
        // connections' non-private profiles are pooled in too — sharing
        // NPC cards is what makes a family group an actual group.
        ViewBag.TaggableProfiles = await LoadTaggableProfilesAsync(userId);

        var vm = new CreatePostViewModel { EventYear = DateTime.UtcNow.Year };

        // Lightweight pre-fill from a query string — used by the
        // Working Index "Write about it" chip to seed the year and a
        // title from a cell value. No body is pre-filled (the writer
        // expands the thought from there).
        if (year.HasValue && year.Value >= 1 && year.Value <= 2200)
        {
            vm.EventYear = year.Value;
        }
        if (!string.IsNullOrWhiteSpace(title))
        {
            vm.Title = title.Trim().Length > 200 ? title.Trim()[..200] : title.Trim();
        }

        // Validate the post-as target before pre-selecting: must be a
        // biographical account this user manages. Bio posts default to Book.
        if (!string.IsNullOrEmpty(postAsUserId))
        {
            var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == postAsUserId);
            if (target != null && target.IsBiographical && target.ManagedByUserId == userId)
            {
                vm.PostAsUserId = postAsUserId;
                vm.LayoutStyle = PostLayoutStyle.Book;
            }
        }

        // Validate the channel pre-select similarly: caller must be the
        // channel's assigned writer (or, via post-as, an admin posting on
        // behalf of the writer). Channel pre-select inherits the channel's
        // DefaultLayoutStyle so the writer doesn't need to remember which
        // style this channel ships in.
        if (channelId.HasValue)
        {
            var ch = await _db.Channels.FirstOrDefaultAsync(c => c.Id == channelId.Value);
            if (ch != null
                && (ch.AdminUserId == userId
                    || (vm.PostAsUserId != null && ch.AdminUserId == vm.PostAsUserId)))
            {
                vm.ChannelId = ch.Id;
                if (ch.DefaultLayoutStyle != PostLayoutStyle.Standard)
                {
                    vm.LayoutStyle = ch.DefaultLayoutStyle;
                }
            }
        }

        // "Connect your memory" deep link: only valid against a published,
        // non-deleted source post the reader can actually see. We seed the
        // event date from the source so the memory lands at the right point
        // on the writer's timeline; the writer can change it.
        if (memoryOfId.HasValue)
        {
            var src = await _db.LifeEventPosts
                .Include(p => p.Channel)
                .FirstOrDefaultAsync(p => p.Id == memoryOfId.Value && !p.IsDraft);
            if (src != null)
            {
                vm.MemoryOfPostId = src.Id;
                vm.MemoryOfPostTitle = !string.IsNullOrWhiteSpace(src.Title)
                    ? src.Title
                    : (src.Channel?.Name ?? "this story");
                vm.EventYear = src.EventYear;
                vm.EventMonth = src.EventMonth;
                vm.EventDay = src.EventDay;
                vm.EventDateIsEstimated = src.EventDateIsEstimated;
                if (string.IsNullOrEmpty(vm.Location)) vm.Location = src.Location;
            }
        }

        return View(vm);
    }

    // POST: /Posts/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(250L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 250L * 1024 * 1024, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Create(CreatePostViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var post = await _postService.CreatePostAsync(userId, model);

        // Crowdsource-a-memory flow: the composer carries an
        // AttachToGroupId from the "+ Your version" link. After the
        // post saves, auto-attach it to that group so the writer's
        // contribution lands in the right family conversation without
        // a second click.
        if (!model.IsDraft && model.AttachToGroupId.HasValue)
        {
            var groupId = model.AttachToGroupId.Value;
            var canAttach = await _db.FamilyGroupMembers
                .AnyAsync(m => m.FamilyGroupId == groupId && m.UserId == userId);
            if (canAttach)
            {
                var dupe = await _db.FamilyGroupPosts
                    .AnyAsync(p => p.FamilyGroupId == groupId && p.LifeEventPostId == post.Id);
                if (!dupe)
                {
                    _db.FamilyGroupPosts.Add(new FamilyGroupPost
                    {
                        FamilyGroupId   = groupId,
                        LifeEventPostId = post.Id,
                        AddedByUserId   = userId
                    });
                    await _db.SaveChangesAsync();
                }
            }
        }

        if (model.IsDraft)
        {
            TempData["Success"] = "Draft saved. Come back any time to finish it.";
            return RedirectToAction(nameof(Drafts));
        }
        // For article-style posts (Journal / Book), send the writer to Edit
        // so they can position photos on the layout grid before the article
        // goes live for readers. The post is published either way; the
        // detour is just for layout polish.
        if (post.LayoutStyle == PostLayoutStyle.Newspaper || post.LayoutStyle == PostLayoutStyle.Book)
        {
            TempData["Success"] = "Article saved. Place each photo on the layout grid to finish.";
            return RedirectToAction(nameof(Edit), new { id = post.Id });
        }
        if (model.AttachToGroupId.HasValue)
        {
            return RedirectToAction("Feed", "FamilyGroups", new { id = model.AttachToGroupId.Value });
        }
        return RedirectToAction("Timeline", new { id = userId });
    }

    // GET: /Posts/Drafts — owner-only list of unpublished posts
    [HttpGet]
    public async Task<IActionResult> Drafts()
    {
        var userId = _userManager.GetUserId(User)!;
        // Include drafts on biographical accounts the current user manages, so
        // an admin posting as a bio profile can find and finish those drafts.
        var drafts = await _db.LifeEventPosts
            .Include(p => p.Owner)
            .Where(p => p.IsDraft && (
                p.OwnerUserId == userId ||
                (p.Owner != null && p.Owner.IsBiographical && p.Owner.ManagedByUserId == userId)
            ))
            .OrderByDescending(p => p.LastEditedAt ?? p.CreatedAt)
            .ToListAsync();
        return View(drafts);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraft(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts.Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id && p.IsDraft);
        if (post != null && post.CanBeManagedBy(userId))
        {
            _db.LifeEventPosts.Remove(post);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Draft deleted.";
        }
        return RedirectToAction(nameof(Drafts));
    }

    // POST: /Posts/Delete/5  — soft-delete (moves the story to the archive)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts.Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (post == null || !post.CanBeManagedBy(userId)) return NotFound();
        post.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Story moved to Deleted Stories. You can restore it from there.";
        return RedirectToAction("Timeline", new { id = post.OwnerUserId });
    }

    // GET: /Posts/Deleted  — archive of soft-deleted stories the user can manage
    // (their own + biographical accounts they administer).
    [HttpGet]
    public async Task<IActionResult> Deleted()
    {
        var userId = _userManager.GetUserId(User)!;
        var trashed = await _db.LifeEventPosts
            .IgnoreQueryFilters()
            .Include(p => p.Owner)
            .Where(p => p.DeletedAt != null && (
                p.OwnerUserId == userId ||
                (p.Owner != null && p.Owner.IsBiographical && p.Owner.ManagedByUserId == userId)
            ))
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync();
        return View(trashed);
    }

    // POST: /Posts/Restore/5  — bring back from the archive
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts
            .IgnoreQueryFilters()
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);
        if (post == null || !post.CanBeManagedBy(userId)) return NotFound();
        post.DeletedAt = null;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Story restored.";
        return RedirectToAction(nameof(Deleted));
    }

    // POST: /Posts/MuteStory/5?days=14 — admin-only soft hide. Sets
    // MutedUntil so the post drops out of feeds (own, friend, public,
    // evergreen pool) for the given number of days. Direct links and the
    // writer's own timeline still surface it. Pass days=0 to clear the
    // mute manually.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MuteStory(int id, int days = 14)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts
            .Include(p => p.Channel)
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (post == null) return NotFound();

        // Authorization: app admins can mute anything; the channel writer
        // can mute their own channel posts; otherwise no.
        var isAppAdmin = User.IsInRole("Admin");
        var isChannelAdmin = post.ChannelId.HasValue
            && post.Channel != null
            && post.Channel.AdminUserId == userId;
        if (!isAppAdmin && !isChannelAdmin) return Forbid();

        if (days <= 0)
        {
            post.MutedUntil = null;
            TempData["Success"] = "Mute cleared. The story is back in feeds.";
        }
        else
        {
            days = Math.Min(days, 365);
            post.MutedUntil = DateTime.UtcNow.AddDays(days);
            TempData["Success"] = $"Story muted for {days} day{(days == 1 ? "" : "s")}. It won't show in feeds until {post.MutedUntil.Value:MMM d, yyyy}.";
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Detail), new { id });
    }

    // POST: /Posts/Republish/5 — admin-only "push back to top". Stamps
    // RepublishedAt = now so feed sorts treat the post as freshly posted.
    // Original CreatedAt stays intact for the byline / archive. Useful
    // for surfacing older channel articles to readers who joined later.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Republish(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDraft);
        if (post == null) return NotFound();

        var isAppAdmin = User.IsInRole("Admin");
        var isChannelAdmin = post.ChannelId.HasValue
            && post.Channel != null
            && post.Channel.AdminUserId == userId;
        if (!isAppAdmin && !isChannelAdmin) return Forbid();

        // Republish only makes sense for channel content — channel posts
        // are the long-lived editorial layer that benefits from rotation.
        if (!post.ChannelId.HasValue)
        {
            TempData["Error"] = "Only channel stories can be re-published.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        post.RepublishedAt = DateTime.UtcNow;
        // Clear any stale mute so the freshly-republished post is visible.
        if (post.MutedUntil.HasValue && post.MutedUntil.Value > DateTime.UtcNow)
        {
            post.MutedUntil = null;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Story re-published — it will surface at the top of feeds again.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    // POST: /Posts/DeleteForever/5  — second-stage permanent removal (irreversible)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteForever(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts
            .IgnoreQueryFilters()
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);
        if (post == null || !post.CanBeManagedBy(userId)) return NotFound();
        _db.LifeEventPosts.Remove(post);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Story permanently deleted.";
        return RedirectToAction(nameof(Deleted));
    }

    // GET /Posts/Biographies — index of every biographical / managed account
    [HttpGet]
    public async Task<IActionResult> Biographies()
    {
        var people = await _db.Users
            .Where(u => u.IsBiographical)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .ToListAsync();

        var ids = people.Select(p => p.Id).ToList();
        var counts = await _db.LifeEventPosts
            .Where(p => ids.Contains(p.OwnerUserId) && !p.IsDraft)
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { OwnerUserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OwnerUserId, x => x.Count);

        ViewBag.PostCounts = counts;
        return View(people);
    }

    // GET: /Posts/Detail/5
    [HttpGet]
    public async Task<IActionResult> Detail(int id, int? groupId = null)
    {
        var post = await _postService.GetPostAsync(id);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var isOwner = currentUserId == post.OwnerUserId;

        if (!isOwner)
        {
            // Public posts are viewable by anyone; otherwise require a connection
            if (post.Visibility != PostVisibility.Public)
            {
                var canView = await _permissionService.CanViewPostsAsync(currentUserId, post.OwnerUserId);
                if (!canView) return Forbid();
            }
        }

        // Group-scoped surface: when the post is opened via a group feed
        // we filter comments to ONLY that group's thread. The post itself
        // is the same data; the conversation around it is per-surface so
        // private group chatter never leaks back to the personal feed.
        // groupId == null → personal-feed surface, comments where
        // FamilyGroupId is null. groupId == N → must be a member of N.
        if (groupId.HasValue)
        {
            var member = await _db.FamilyGroupMembers
                .AnyAsync(m => m.FamilyGroupId == groupId.Value && m.UserId == currentUserId);
            if (!member && !User.IsInRole("Admin"))
            {
                // Not a member of that group — silently drop the surface.
                groupId = null;
            }
        }
        ViewBag.GroupId = groupId;
        post.Comments = post.Comments
            .Where(c => c.FamilyGroupId == groupId)
            .ToList();

        string? diffHtml = null;
        if (post.Versions.Count >= 2)
        {
            var versions = post.Versions.OrderByDescending(v => v.VersionNumber).ToList();
            diffHtml = _diffService.ComputeDiffHtml(versions[1].BodySnapshot, versions[0].BodySnapshot);
        }

        // Collect every user id we need to display: post tag-list + every
        // comment's mention list. Then batch-load them in ONE query and
        // build view models from the lookup. Old code did one DB hit per
        // tag and per mention — could easily be 100+ queries on a busy post.
        var allReferencedIds = new HashSet<string>();
        if (!string.IsNullOrEmpty(post.TaggedUserIds))
        {
            foreach (var t in post.TaggedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                allReferencedIds.Add(t.Trim());
        }
        foreach (var comment in post.Comments)
        {
            if (string.IsNullOrEmpty(comment.MentionedUserIds)) continue;
            foreach (var m in comment.MentionedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                allReferencedIds.Add(m.Trim());
        }

        Dictionary<string, ApplicationUser> referencedById;
        if (allReferencedIds.Count == 0)
        {
            referencedById = new Dictionary<string, ApplicationUser>();
        }
        else
        {
            referencedById = await _db.Users
                .Where(u => allReferencedIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);
        }

        var taggedUsers = new List<TaggedUserViewModel>();
        if (!string.IsNullOrEmpty(post.TaggedUserIds))
        {
            foreach (var tagId in post.TaggedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (referencedById.TryGetValue(tagId.Trim(), out var u))
                {
                    taggedUsers.Add(new TaggedUserViewModel
                    {
                        UserId = u.Id,
                        DisplayName = u.DisplayName ?? u.UserName!
                    });
                }
            }
        }

        // People-profile tags (NPC cards for non-members). Stored in a
        // parallel comma-separated column so a profile can be tagged
        // independently of any AspNetUsers row.
        var taggedProfiles = new List<TaggedProfileViewModel>();
        if (!string.IsNullOrEmpty(post.TaggedProfileIds))
        {
            var profileIds = post.TaggedProfileIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                .Where(n => n > 0)
                .ToList();
            if (profileIds.Count > 0)
            {
                var profiles = await _db.PersonProfiles
                    .Where(p => profileIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);
                foreach (var pid in profileIds)
                {
                    if (profiles.TryGetValue(pid, out var pp))
                    {
                        taggedProfiles.Add(new TaggedProfileViewModel
                        {
                            ProfileId = pp.Id,
                            DisplayName = pp.DisplayName,
                            AvatarUrl = pp.AvatarUrl,
                            LinkedUserId = pp.LinkedUserId
                        });
                    }
                }
            }
        }

        var commentMentions = new Dictionary<int, List<TaggedUserViewModel>>();
        foreach (var comment in post.Comments)
        {
            if (string.IsNullOrEmpty(comment.MentionedUserIds)) continue;
            var mentionedUsers = new List<TaggedUserViewModel>();
            foreach (var mid in comment.MentionedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (referencedById.TryGetValue(mid.Trim(), out var u))
                {
                    mentionedUsers.Add(new TaggedUserViewModel
                    {
                        UserId = u.Id,
                        UserName = u.UserName!,
                        DisplayName = u.DisplayName ?? u.UserName!
                    });
                }
            }
            commentMentions[comment.Id] = mentionedUsers;
        }

        // Friends list for @mention autocomplete
        var friendList = await _friendService.GetFriendListAsync(currentUserId);
        var taggableFriends = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        // Comment likes — one query, group by comment, mark which the current user has liked.
        var commentIds = post.Comments.Select(c => c.Id).ToList();
        var commentLikes = new Dictionary<int, (int Count, bool LikedByMe)>();
        if (commentIds.Count > 0)
        {
            var rows = await _db.CommentLikes
                .Where(l => commentIds.Contains(l.CommentId))
                .Select(l => new { l.CommentId, l.UserId })
                .ToListAsync();
            foreach (var grp in rows.GroupBy(r => r.CommentId))
            {
                commentLikes[grp.Key] = (grp.Count(), grp.Any(x => x.UserId == currentUserId));
            }
            foreach (var cid in commentIds)
            {
                if (!commentLikes.ContainsKey(cid)) commentLikes[cid] = (0, false);
            }
        }

        // "Connect your memory" linkage. Count of memories pointing back to
        // this post (excludes drafts) and, if this post is itself a memory,
        // hydrate the source so the chip can render.
        var connectedMemoryCount = await _db.LifeEventPosts
            .CountAsync(p => p.MemoryOfPostId == post.Id && !p.IsDraft);
        LifeEventPost? memoryOf = null;
        if (post.MemoryOfPostId.HasValue)
        {
            memoryOf = await _db.LifeEventPosts
                .Include(p => p.Channel)
                .Include(p => p.Owner)
                .FirstOrDefaultAsync(p => p.Id == post.MemoryOfPostId.Value);
        }

        // Photo-tag permission mirrors MediaController.AddPersonTag's
        // server-side check: owner, admin, or family-tier viewer.
        var viewerTierForPost = isOwner ? FriendTier.Family
                              : await _permissionService.GetViewerTierAsync(currentUserId, post.OwnerUserId);
        var canTagPhotos = isOwner || User.IsInRole("Admin") || viewerTierForPost == FriendTier.Family;

        var vm = new PostDetailViewModel
        {
            Post = post,
            DiffHtml = diffHtml,
            IsOwner = isOwner,
            CanManage = post.CanBeManagedBy(currentUserId),
            CanComment = isOwner || await _permissionService.CanCommentOnPostAsync(currentUserId, post),
            LikeCount = post.Likes.Count,
            CurrentUserLiked = post.Likes.Any(l => l.UserId == currentUserId),
            CurrentUserReaction = post.Likes.FirstOrDefault(l => l.UserId == currentUserId)?.ReactionType,
            TaggedUsers = taggedUsers,
            TaggedProfiles = taggedProfiles,
            Comments = post.Comments.OrderBy(c => c.CreatedAt).ToList(),
            MediaPersonTags = await LoadMediaTagsForPostAsync(post.Id),
            TaggableFriends = taggableFriends,
            TaggableProfiles = canTagPhotos ? await LoadTaggableProfilesAsync(currentUserId) : new(),
            CanTagPhotos = canTagPhotos,
            CommentMentions = commentMentions,
            CommentLikes = commentLikes,
            ConnectedMemoryCount = connectedMemoryCount,
            MemoryOf = memoryOf
        };

        return View(vm);
    }

    // GET: /Posts/Memories/5 — list of all posts linked to this one via
    // "Connect your memory to this story". Visible to anyone who can see
    // the source article; the listed memories are filtered through the
    // viewer's normal visibility rules so private memories stay private.
    [HttpGet]
    public async Task<IActionResult> Memories(int id)
    {
        var source = await _db.LifeEventPosts
            .Include(p => p.Channel)
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDraft);
        if (source == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User);
        var memories = await _db.LifeEventPosts
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Where(p => p.MemoryOfPostId == id && !p.IsDraft)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var visible = new List<LifeEventPost>();
        foreach (var m in memories)
        {
            if (m.Visibility == PostVisibility.Public)
            {
                visible.Add(m);
            }
            else if (!string.IsNullOrEmpty(currentUserId))
            {
                if (currentUserId == m.OwnerUserId
                    || await _permissionService.CanViewPostsAsync(currentUserId, m.OwnerUserId))
                {
                    visible.Add(m);
                }
            }
        }

        ViewBag.SourcePost = source;
        return View(visible);
    }

    // POST: /Posts/Translate/5?to=en
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("translate")]
    public async Task<IActionResult> Translate(int id, string? to = null)
    {
        var post = await _postService.GetPostAsync(id);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var isOwner = currentUserId == post.OwnerUserId;
        if (!isOwner && post.Visibility != PostVisibility.Public)
        {
            var canView = await _permissionService.CanViewPostsAsync(currentUserId, post.OwnerUserId);
            if (!canView) return Forbid();
        }

        // Resolve target: explicit ?to= wins, else user's preferred, else English.
        var target = !string.IsNullOrWhiteSpace(to) ? to! : null;
        if (target == null)
        {
            var me = await _userManager.GetUserAsync(User);
            target = !string.IsNullOrWhiteSpace(me?.PreferredReadingLanguage) ? me!.PreferredReadingLanguage : "en";
        }

        try
        {
            var result = await _translation.TranslatePostAsync(id, target!);
            return Json(new
            {
                title = result.Title,
                body = result.Body,
                fromLang = result.FromLanguage,
                comments = result.Comments.Select(c => new { id = c.CommentId, body = c.Body })
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "translation_failed", detail = ex.Message });
        }
    }

    // POST: /Posts/LikeComment/5  — toggles a heart on the comment for the current user
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> LikeComment(int id)
    {
        var comment = await _db.Comments.Include(c => c.Post).FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null) return NotFound();

        // Same visibility rule as the post: must be owner, post is public, or have view permission.
        var currentUserId = _userManager.GetUserId(User)!;
        var isPostOwner = currentUserId == comment.Post.OwnerUserId;
        if (!isPostOwner && comment.Post.Visibility != PostVisibility.Public)
        {
            var canView = await _permissionService.CanViewPostsAsync(currentUserId, comment.Post.OwnerUserId);
            if (!canView) return Forbid();
        }

        var existing = await _db.CommentLikes.FirstOrDefaultAsync(l => l.CommentId == id && l.UserId == currentUserId);
        bool likedNow;
        if (existing == null)
        {
            _db.CommentLikes.Add(new CommentLike { CommentId = id, UserId = currentUserId, CreatedAt = DateTime.UtcNow });
            likedNow = true;
        }
        else
        {
            _db.CommentLikes.Remove(existing);
            likedNow = false;
        }
        await _db.SaveChangesAsync();

        var count = await _db.CommentLikes.CountAsync(l => l.CommentId == id);
        return Json(new { liked = likedNow, count });
    }

    // GET: /Posts/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var post = await _postService.GetPostAsync(id);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        if (!post.CanBeManagedBy(currentUserId)) return Forbid();

        var friendList = await _friendService.GetFriendListAsync(currentUserId);
        var taggable = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        var taggableProfiles = await LoadTaggableProfilesAsync(currentUserId);

        var currentTagIds = string.IsNullOrEmpty(post.TaggedUserIds)
            ? new List<string>()
            : post.TaggedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var currentTagged = currentTagIds
            .Select(id => taggable.FirstOrDefault(t => t.UserId == id))
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();

        var currentProfileIds = string.IsNullOrEmpty(post.TaggedProfileIds)
            ? new List<int>()
            : post.TaggedProfileIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToList();
        var currentTaggedProfiles = currentProfileIds
            .Select(pid => taggableProfiles.FirstOrDefault(p => p.ProfileId == pid))
            .Where(p => p != null).Select(p => p!).ToList();

        // If the layout was never explicitly set (Standard) but this post is
        // in a channel or owned by a biographical account, suggest the
        // matching style on first edit so existing posts pick up the new
        // look without the writer having to figure out the picker.
        var layout = post.LayoutStyle;
        if (layout == PostLayoutStyle.Standard)
        {
            if (post.ChannelId.HasValue) layout = PostLayoutStyle.Newspaper;
            else if (post.Owner != null && post.Owner.IsBiographical) layout = PostLayoutStyle.Book;
        }

        var model = new EditPostViewModel
        {
            PostId = post.Id,
            Title = post.Title,
            Body = post.Body,
            EventYear = post.EventYear,
            EventMonth = post.EventMonth,
            EventDay = post.EventDay,
            EventDateIsEstimated = post.EventDateIsEstimated,
            Visibility = post.Visibility,
            Location = post.Location,
            TaggedUserIds = currentTagIds,
            TaggedProfileIds = currentProfileIds,
            TaggableFriends = taggable,
            TaggableProfiles = taggableProfiles,
            IsDraft = post.IsDraft,
            LayoutStyle = layout
        };

        ViewBag.CurrentTagged = currentTagged;
        ViewBag.CurrentTaggedProfiles = currentTaggedProfiles;
        ViewBag.ExistingMedia = post.Media
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .ToList();

        // Photo person-tags grouped by media id so the view can render
        // the existing overlays + the picker uses the same friend / profile
        // pools as the body-level tag widget.
        var mediaIds = post.Media.Select(m => m.Id).ToList();
        ViewBag.MediaPersonTags = mediaIds.Count == 0
            ? new Dictionary<int, List<MediaPersonTag>>()
            : (await _db.MediaPersonTags
                .Where(t => mediaIds.Contains(t.PostMediaId))
                .Include(t => t.TargetUser)
                .Include(t => t.TargetProfile)
                .ToListAsync())
                .GroupBy(t => t.PostMediaId)
                .ToDictionary(g => g.Key, g => g.ToList());

        return View(model);
    }

    // POST: /Posts/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(250L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 250L * 1024 * 1024, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Edit(EditPostViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var uid = _userManager.GetUserId(User)!;
            var fl = await _friendService.GetFriendListAsync(uid);
            model.TaggableFriends = fl.Friends.Select(f => new TaggableFriendViewModel
            {
                UserId = f.User.Id,
                DisplayName = f.User.DisplayName ?? f.User.UserName!
            }).ToList();
            model.TaggableProfiles = await LoadTaggableProfilesAsync(uid);
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var post = await _postService.EditPostAsync(model.PostId, userId, model);
        if (post == null) return Forbid();

        if (model.IsDraft)
        {
            TempData["Success"] = "Draft saved.";
            return RedirectToAction(nameof(Drafts));
        }
        return RedirectToAction("Detail", new { id = model.PostId });
    }

    // POST: /Posts/AddComment
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(AddCommentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            // Surface why instead of silently bouncing — empty body, etc.
            var reasons = string.Join("; ",
                ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            TempData["Error"] = string.IsNullOrEmpty(reasons)
                ? "Couldn't post the comment. Please try again."
                : "Couldn't post the comment: " + reasons;
            return RedirectToAction("Detail", new { id = model.PostId });
        }

        var currentUserId = _userManager.GetUserId(User)!;
        var post = await _postService.GetPostAsync(model.PostId);
        if (post == null) return NotFound();

        var canComment = await _permissionService.CanCommentOnPostAsync(currentUserId, post);
        if (!canComment)
        {
            TempData["Error"] = "You don't have permission to comment on this post.";
            return RedirectToAction("Detail", new { id = model.PostId });
        }

        if (model.EventYear == null)
        {
            model.EventYear = post.EventYear;
            model.EventMonth ??= post.EventMonth;
            model.EventDay ??= post.EventDay;
        }

        // Honor the group-surface scope even if the form somehow posts a
        // groupId for a group the user isn't a member of — drop it so the
        // comment can't be smuggled into a group conversation by handcrafted POST.
        if (model.FamilyGroupId.HasValue)
        {
            var member = await _db.FamilyGroupMembers
                .AnyAsync(m => m.FamilyGroupId == model.FamilyGroupId.Value && m.UserId == currentUserId);
            if (!member) model.FamilyGroupId = null;
        }
        var newComment = await _postService.AddCommentAsync(currentUserId, model);
        await FanoutCommentNotificationsAsync(post, newComment, currentUserId);
        return RedirectToAction("Detail", new { id = model.PostId, groupId = model.FamilyGroupId });
    }

    // POST: /Posts/EditComment  — author can edit their comment body in place.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditComment(int id, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return BadRequest(new { error = "empty" });

        var comment = await _db.Comments.Include(c => c.Post).FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        if (comment.AuthorUserId != currentUserId) return Forbid();

        comment.Body = MyStoryTold.Helpers.BodyRenderer.Sanitize(body);
        await _db.SaveChangesAsync();

        // Return the rendered HTML so the JS can drop it back in without a reload.
        var html = MyStoryTold.Helpers.BodyRenderer.RenderBody(comment.Body).ToString();
        return Json(new { id = comment.Id, body = comment.Body, html });
    }

    // POST: /Posts/DeleteComment  — author or post owner can delete a comment.
    // Replies (children of the deleted comment) are removed alongside.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComment(int id)
    {
        var comment = await _db.Comments.Include(c => c.Post).FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var isAuthor = comment.AuthorUserId == currentUserId;
        var isPostOwner = comment.Post.OwnerUserId == currentUserId;
        if (!isAuthor && !isPostOwner) return Forbid();

        // Drop direct replies, then the comment itself, in a single transaction.
        var replyIds = await _db.Comments
            .Where(c => c.ParentCommentId == id)
            .Select(c => c.Id)
            .ToListAsync();
        if (replyIds.Count > 0)
        {
            await _db.Comments.Where(c => replyIds.Contains(c.Id)).ExecuteDeleteAsync();
        }
        _db.Comments.Remove(comment);
        await _db.SaveChangesAsync();

        return Json(new { id, deletedReplies = replyIds });
    }

    // GET: /Posts/CommentsAjax/5  (returns JSON list of top-level comments + replies for inline expand)
    [HttpGet]
    public async Task<IActionResult> CommentsAjax(int id)
    {
        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post == null) return NotFound();

        var comments = await _db.Comments
            .Where(c => c.PostId == id)
            .Include(c => c.Author)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var data = comments.Select(c => new
        {
            id = c.Id,
            parentId = c.ParentCommentId,
            body = c.Body,
            createdAt = c.CreatedAt.ToString("MMM d, yyyy h:mm tt"),
            authorName = c.Author?.DisplayName ?? c.Author?.UserName ?? "Unknown",
            authorInitial = (c.Author?.FirstName?[0].ToString() ?? c.Author?.UserName?[0].ToString() ?? "?").ToUpper(),
            authorPhoto = c.Author?.ProfilePhotoUrl
        });
        return Json(data);
    }

    // POST: /Posts/AddCommentAjax  (returns JSON for the new comment for inline append)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> AddCommentAjax(int postId, string body, int? parentCommentId)
    {
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Empty");

        var post = await _postService.GetPostAsync(postId);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var canComment = await _permissionService.CanCommentOnPostAsync(currentUserId, post);
        if (!canComment) return Forbid();

        var comment = await _postService.AddCommentAsync(currentUserId, new AddCommentViewModel
        {
            PostId = postId,
            Body = body,
            ParentCommentId = parentCommentId,
            EventYear = post.EventYear,
            EventMonth = post.EventMonth,
            EventDay = post.EventDay
        });
        await FanoutCommentNotificationsAsync(post, comment, currentUserId);

        var user = await _userManager.FindByIdAsync(currentUserId);
        return Json(new
        {
            id = comment.Id,
            parentId = comment.ParentCommentId,
            body = comment.Body,
            createdAt = comment.CreatedAt.ToString("MMM d, yyyy h:mm tt"),
            authorName = user?.DisplayName ?? user?.UserName ?? "You",
            authorInitial = (user?.FirstName?[0].ToString() ?? user?.UserName?[0].ToString() ?? "?").ToUpper(),
            authorPhoto = user?.ProfilePhotoUrl
        });
    }

    // POST: /Posts/ToggleLike/5  (full-page redirect, used from Detail)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLike(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _postService.ToggleLikeAsync(id, userId);
        return RedirectToAction("Detail", new { id });
    }

    // POST: /Posts/ToggleLikeAjax/5  (returns JSON {liked, count} for in-place update)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> ToggleLikeAjax(int id, ReactionType reactionType = ReactionType.Heart)
    {
        var userId = _userManager.GetUserId(User)!;
        var (reaction, count) = await _postService.ToggleReactionAsync(id, userId, reactionType);
        return Json(new
        {
            liked  = reaction != null,
            reaction = reaction.HasValue ? (int)reaction.Value : (int?)null,
            count
        });
    }

    // POST: /Posts/QuickPost
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(250L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 250L * 1024 * 1024, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> QuickPost(string body, int eventYear, int? eventMonth, int? eventDay, PostVisibility visibility, string? location, string? musicUrl, List<string>? taggedUserIds, List<IFormFile>? Images, IFormFile? Video, string? returnTo)
    {
        if (string.IsNullOrWhiteSpace(body) || eventYear == 0)
        {
            TempData["Error"] = "Story and year are required.";
            return RedirectToAction("Index", "Home");
        }

        var userId = _userManager.GetUserId(User)!;
        await _postService.CreatePostAsync(userId, new CreatePostViewModel
        {
            Body = body,
            EventYear = eventYear,
            EventMonth = eventMonth,
            EventDay = eventDay,
            Visibility = visibility,
            Location = location,
            MusicUrl = musicUrl,
            TaggedUserIds = taggedUserIds,
            Images = Images,
            Video = Video
        });

        TempData["Success"] = "Story added!";
        if (returnTo == "timeline")
            return RedirectToAction("Timeline", new { id = userId });
        return RedirectToAction("Index", "Home");
    }

    // GET: /Posts/Feed — redirects to Home which is now the combined feed
    [HttpGet]
    public IActionResult Feed() => RedirectToAction("Index", "Home");

    [HttpGet]
    [ActionName("FeedFull")]
    public async Task<IActionResult> FeedFull()
    {
        var userId = _userManager.GetUserId(User)!;
        var posts = await _postService.GetFeedPostsAsync(userId);

        var feedPosts = new List<FeedPostViewModel>();
        foreach (var post in posts)
        {
            var taggedUsers = new List<TaggedUserViewModel>();
            if (!string.IsNullOrEmpty(post.TaggedUserIds))
            {
                foreach (var tagId in post.TaggedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var taggedUser = await _userManager.FindByIdAsync(tagId.Trim());
                    if (taggedUser != null)
                        taggedUsers.Add(new TaggedUserViewModel
                        {
                            UserId = taggedUser.Id,
                            UserName = taggedUser.UserName!,
                            DisplayName = taggedUser.DisplayName ?? taggedUser.UserName!
                        });
                }
            }

            feedPosts.Add(new FeedPostViewModel
            {
                Post = post,
                LikeCount = post.Likes.Count,
                CurrentUserLiked = post.Likes.Any(l => l.UserId == userId),
                TaggedUsers = taggedUsers
            });
        }

        return View(new FeedViewModel { Posts = feedPosts });
    }

    // GET: /Posts/Tagged
    [HttpGet]
    public async Task<IActionResult> Tagged()
    {
        var userId = _userManager.GetUserId(User)!;

        var taggedPosts = await _db.LifeEventPosts
            .Where(p => p.TaggedUserIds != null && p.TaggedUserIds.Contains(userId))
            .Include(p => p.Owner)
            .Include(p => p.Comments)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var feedPosts = taggedPosts.Select(p => new FeedPostViewModel
        {
            Post = p,
            LikeCount = p.Likes.Count,
            CurrentUserLiked = p.Likes.Any(l => l.UserId == userId)
        }).ToList();

        return View(new FeedViewModel { Posts = feedPosts });
    }

    // POST: /Posts/UploadPastedImage — used by paste-image JS
    [HttpPost]
    public async Task<IActionResult> UploadPastedImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "File too large" });

        // Magic-byte sniff — don't trust the browser-supplied content type.
        using var stream = file.OpenReadStream();
        var sig = await MyStoryTold.Helpers.FileSignatures.DetectAsync(stream);
        if (!MyStoryTold.Helpers.FileSignatures.IsImage(sig))
            return BadRequest(new { error = "Unsupported file. Only JPEG, PNG, GIF, or WebP images are accepted." });

        var ext = MyStoryTold.Helpers.FileSignatures.ExtensionFor(sig);
        var canonicalMime = MyStoryTold.Helpers.FileSignatures.MimeFor(sig);
        var fileName = $"{Guid.NewGuid()}{ext}";

        if (stream.CanSeek) stream.Position = 0;
        var url = await _files.UploadAsync(stream, "", fileName, canonicalMime);
        return Ok(new { url });
    }

    // POST: /Posts/Reorder
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(int postId, int newOrder)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _postService.GetPostAsync(postId);
        if (post == null) return NotFound();

        var canReorder = await _permissionService.CanReorderAsync(userId, post.OwnerUserId);
        if (!canReorder) return Forbid();

        await _postService.ReorderPostAsync(postId, newOrder, userId);
        return RedirectToAction("Timeline", new { id = post.OwnerUserId });
    }
}
