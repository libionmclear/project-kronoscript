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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public PostsController(
        IPostService postService,
        IPermissionService permissionService,
        IDiffService diffService,
        IFriendService friendService,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db)
    {
        _postService = postService;
        _permissionService = permissionService;
        _diffService = diffService;
        _friendService = friendService;
        _userManager = userManager;
        _db = db;
    }

    // GET: /Posts/Timeline/{userId}?sort=created|event
    [HttpGet]
    public async Task<IActionResult> Timeline(string id, string sort = "created")
    {
        var profileUser = await _userManager.FindByIdAsync(id);
        if (profileUser == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var isOwner = currentUserId == id;

        if (!isOwner)
        {
            var canView = await _permissionService.CanViewPostsAsync(currentUserId, id);
            if (!canView) return Forbid();
        }

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
    public async Task<IActionResult> Create(CreatePostViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var post = await _postService.CreatePostAsync(userId, model);
        return RedirectToAction("Timeline", new { id = userId });
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
            var canView = await _permissionService.CanViewPostsAsync(currentUserId, post.OwnerUserId);
            if (!canView) return Forbid();
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
            TaggedUsers = taggedUsers,
            Comments = post.Comments.OrderBy(c => c.CreatedAt).ToList(),
            TaggableFriends = taggableFriends,
            CommentMentions = commentMentions
        };

        return View(vm);
    }

    // GET: /Posts/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var post = await _postService.GetPostAsync(id);
        if (post == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        if (post.OwnerUserId != currentUserId) return Forbid();

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
            Location = post.Location
        };

        return View(model);
    }

    // POST: /Posts/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditPostViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var post = await _postService.EditPostAsync(model.PostId, userId, model);
        if (post == null) return Forbid();

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

    // POST: /Posts/ToggleLike/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLike(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _postService.ToggleLikeAsync(id, userId);
        return RedirectToAction("Detail", new { id });
    }

    // POST: /Posts/QuickPost
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickPost(string body, int eventYear, int? eventMonth, int? eventDay, PostVisibility visibility)
    {
        if (string.IsNullOrWhiteSpace(body) || eventYear < 1)
        {
            TempData["Error"] = "Story and year are required.";
            return RedirectToAction(nameof(Feed));
        }

        var userId = _userManager.GetUserId(User)!;
        await _postService.CreatePostAsync(userId, new CreatePostViewModel
        {
            Body = body,
            EventYear = eventYear,
            EventMonth = eventMonth,
            EventDay = eventDay,
            Visibility = visibility
        });

        TempData["Success"] = "Story added!";
        return RedirectToAction(nameof(Feed));
    }

    // GET: /Posts/Feed
    [HttpGet]
    public async Task<IActionResult> Feed()
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
