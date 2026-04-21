using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public PostsController(
        IPostService postService,
        IPermissionService permissionService,
        IDiffService diffService,
        IFriendService friendService,
        ITranslationService translation,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IWebHostEnvironment env)
    {
        _postService = postService;
        _permissionService = permissionService;
        _diffService = diffService;
        _friendService = friendService;
        _translation = translation;
        _userManager = userManager;
        _db = db;
        _env = env;
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

            postCards.Add(new PostCardViewModel
            {
                Post = post,
                DiffHtml = diffHtml,
                LikeCount = post.Likes.Count,
                CurrentUserLiked = post.Likes.Any(l => l.UserId == currentUserId),
                CurrentUserReaction = post.Likes.FirstOrDefault(l => l.UserId == currentUserId)?.ReactionType,
                TaggedUsers = taggedUsers
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
            CanComment = isOwner || tier is FriendTier.Friend or FriendTier.Family,
            CanReorder = isOwner || tier == FriendTier.Family,
            SortBy = sort,
            TaggableFriends = taggable
        };

        return View(vm);
    }

    // GET: /Posts/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var userId = _userManager.GetUserId(User)!;
        var friendList = await _friendService.GetFriendListAsync(userId);
        ViewBag.TaggableFriends = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        return View(new CreatePostViewModel { EventYear = DateTime.UtcNow.Year });
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
        if (model.IsDraft)
        {
            TempData["Success"] = "Draft saved. Come back any time to finish it.";
            return RedirectToAction(nameof(Drafts));
        }
        return RedirectToAction("Timeline", new { id = userId });
    }

    // GET: /Posts/Drafts — owner-only list of unpublished posts
    [HttpGet]
    public async Task<IActionResult> Drafts()
    {
        var userId = _userManager.GetUserId(User)!;
        var drafts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId && p.IsDraft)
            .OrderByDescending(p => p.LastEditedAt ?? p.CreatedAt)
            .ToListAsync();
        return View(drafts);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraft(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == userId && p.IsDraft);
        if (post != null)
        {
            _db.LifeEventPosts.Remove(post);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Draft deleted.";
        }
        return RedirectToAction(nameof(Drafts));
    }

    // GET: /Posts/Detail/5
    [HttpGet]
    public async Task<IActionResult> Detail(int id)
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
                        DisplayName = taggedUser.DisplayName ?? taggedUser.UserName!
                    });
            }
        }

        // Resolve comment mentions
        var commentMentions = new Dictionary<int, List<TaggedUserViewModel>>();
        foreach (var comment in post.Comments)
        {
            if (string.IsNullOrEmpty(comment.MentionedUserIds)) continue;
            var mentionedUsers = new List<TaggedUserViewModel>();
            foreach (var mid in comment.MentionedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var mu = await _userManager.FindByIdAsync(mid.Trim());
                if (mu != null)
                    mentionedUsers.Add(new TaggedUserViewModel
                    {
                        UserId = mu.Id,
                        UserName = mu.UserName!,
                        DisplayName = mu.DisplayName ?? mu.UserName!
                    });
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

        var vm = new PostDetailViewModel
        {
            Post = post,
            DiffHtml = diffHtml,
            IsOwner = isOwner,
            CanComment = isOwner || await _permissionService.CanCommentAsync(currentUserId, post.OwnerUserId),
            LikeCount = post.Likes.Count,
            CurrentUserLiked = post.Likes.Any(l => l.UserId == currentUserId),
            CurrentUserReaction = post.Likes.FirstOrDefault(l => l.UserId == currentUserId)?.ReactionType,
            TaggedUsers = taggedUsers,
            Comments = post.Comments.OrderBy(c => c.CreatedAt).ToList(),
            TaggableFriends = taggableFriends,
            CommentMentions = commentMentions
        };

        return View(vm);
    }

    // POST: /Posts/Translate/5?to=en
    [HttpPost]
    [ValidateAntiForgeryToken]
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

    // GET: /Posts/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var post = await _postService.GetPostAsync(id);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        if (post.OwnerUserId != currentUserId) return Forbid();

        var friendList = await _friendService.GetFriendListAsync(currentUserId);
        var taggable = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        var currentTagIds = string.IsNullOrEmpty(post.TaggedUserIds)
            ? new List<string>()
            : post.TaggedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var currentTagged = currentTagIds
            .Select(id => taggable.FirstOrDefault(t => t.UserId == id))
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();

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
            TaggableFriends = taggable,
            IsDraft = post.IsDraft
        };

        ViewBag.CurrentTagged = currentTagged;
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
        if (!ModelState.IsValid) return RedirectToAction("Detail", new { id = model.PostId });

        var currentUserId = _userManager.GetUserId(User)!;
        var post = await _postService.GetPostAsync(model.PostId);
        if (post == null) return NotFound();

        var canComment = await _permissionService.CanCommentAsync(currentUserId, post.OwnerUserId);
        if (!canComment) return Forbid();

        if (model.EventYear == null)
        {
            model.EventYear = post.EventYear;
            model.EventMonth ??= post.EventMonth;
            model.EventDay ??= post.EventDay;
        }

        await _postService.AddCommentAsync(currentUserId, model);
        return RedirectToAction("Detail", new { id = model.PostId });
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
    public async Task<IActionResult> AddCommentAjax(int postId, string body, int? parentCommentId)
    {
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Empty");

        var post = await _postService.GetPostAsync(postId);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var canComment = await _permissionService.CanCommentAsync(currentUserId, post.OwnerUserId);
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
            .Include(p => p.Likes)
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

        var allowed = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowed.Contains(file.ContentType.ToLower()))
            return BadRequest(new { error = "Unsupported type" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "File too large" });

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var fileName = $"{Guid.NewGuid()}{ext}";

        using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            await file.CopyToAsync(stream);

        return Ok(new { url = $"/uploads/{fileName}" });
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
