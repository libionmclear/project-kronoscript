using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFriendService _friendService;
    private readonly IPostService _postService;
    private readonly ApplicationDbContext _db;

    public HomeController(
        ILogger<HomeController> logger,
        UserManager<ApplicationUser> userManager,
        IFriendService friendService,
        IPostService postService,
        ApplicationDbContext db)
    {
        _logger = logger;
        _userManager = userManager;
        _friendService = friendService;
        _postService = postService;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        if (!User.Identity!.IsAuthenticated)
            return View();

        var userId = _userManager.GetUserId(User)!;
        var friendList = await _friendService.GetFriendListAsync(userId);

        // Tier counts
        var friendsCount = friendList.Friends.Count(f => f.Tier == FriendTier.Friend);
        var acquaintancesCount = friendList.Friends.Count(f => f.Tier == FriendTier.Acquaintance);
        var familyCount = friendList.Friends.Count(f => f.Tier == FriendTier.Family);

        // Tagged count
        var taggedCount = await _db.LifeEventPosts
            .Where(p => p.TaggedUserIds != null && p.TaggedUserIds.Contains(userId))
            .CountAsync();

        // Active friends (posted in last 30 days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var friendIds = friendList.Friends.Select(f => f.User.Id).ToList();
        var recentPosterIds = await _db.LifeEventPosts
            .Where(p => friendIds.Contains(p.OwnerUserId) && p.CreatedAt >= thirtyDaysAgo)
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { UserId = g.Key, LastPosted = g.Max(p => p.CreatedAt) })
            .ToListAsync();

        var activeFriends = friendList.Friends
            .Select(f => new ActiveFriendViewModel
            {
                User = f.User,
                LastPostedAt = recentPosterIds.FirstOrDefault(r => r.UserId == f.User.Id)?.LastPosted
            })
            .Where(f => f.LastPostedAt != null)
            .OrderByDescending(f => f.LastPostedAt)
            .Take(8)
            .ToList();

        // Recent feed: own posts + friends posts mixed
        var ownPosts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .ToListAsync();

        var friendPosts = await _postService.GetFeedPostsAsync(userId);

        var allPosts = ownPosts
            .Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId),
                CurrentUserReaction = p.Likes.FirstOrDefault(l => l.UserId == userId)?.ReactionType
            })
            .Concat(friendPosts.Take(30).Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId),
                CurrentUserReaction = p.Likes.FirstOrDefault(l => l.UserId == userId)?.ReactionType
            }))
            .OrderByDescending(p => p.Post.CreatedAt)
            .Take(30)
            .ToList();

        var ownPostCount = await _db.LifeEventPosts.CountAsync(p => p.OwnerUserId == userId);

        var currentUser = await _userManager.FindByIdAsync(userId);
        ViewBag.GreetingName = !string.IsNullOrWhiteSpace(currentUser?.FirstName)
            ? currentUser!.FirstName
            : (currentUser?.UserName ?? User.Identity!.Name);

        // On This Day: user's own past posts matching today's month/day
        var today = DateTime.UtcNow;
        var onThisDay = new List<LifeEventPost>();
        if (ownPostCount > 0)
        {
            onThisDay = await _db.LifeEventPosts
                .Where(p => p.OwnerUserId == userId
                            && p.EventMonth == today.Month
                            && p.EventDay == today.Day
                            && p.EventYear < today.Year)
                .Include(p => p.Owner)
                .OrderByDescending(p => p.EventYear)
                .Take(3)
                .ToListAsync();
        }

        var vm = new DashboardViewModel
        {
            RecentPosts = allPosts,
            FriendsCount = friendsCount,
            AcquaintancesCount = acquaintancesCount,
            FamilyCount = familyCount,
            TaggedCount = taggedCount,
            ActiveFriends = activeFriends,
            IsNewUser = ownPostCount == 0,
            OnThisDay = onThisDay
        };

        ViewBag.TaggableFriends = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Feedback(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["FeedbackError"] = "Please enter a message.";
            return RedirectToAction("Index");
        }

        var userId = _userManager.GetUserId(User)!;
        var admin = await _userManager.FindByNameAsync("kronoadmin");
        if (admin == null)
        {
            TempData["FeedbackError"] = "Feedback inbox is not available right now.";
            return RedirectToAction("Index");
        }

        try
        {
            _db.Messages.Add(new Message
            {
                SenderUserId = userId,
                RecipientUserId = admin.Id,
                Body = "[Feedback] " + body.Trim(),
                SentAt = DateTime.UtcNow,
                IsRead = false
            });
            await _db.SaveChangesAsync();
            TempData["FeedbackSuccess"] = "Thanks! Your feedback was sent to Kronoadmin.";
        }
        catch (Exception ex)
        {
            TempData["FeedbackError"] = "Could not send feedback: " + ex.Message;
        }

        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Invite()
    {
        return View(new InviteViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var token = Guid.NewGuid().ToString("N");

        var invitation = new Invitation
        {
            Token = token,
            InviterUserId = userId,
            InviteeEmail = model.Email,
            Message = model.Message,
            CreatedAt = DateTime.UtcNow
        };

        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync();

        TempData["InviteToken"] = token;
        TempData["InviteEmail"] = model.Email;
        TempData["InviteMessage"] = model.Message;
        return RedirectToAction("InviteSent");
    }

    [HttpGet]
    public IActionResult InviteSent()
    {
        ViewBag.Token = TempData["InviteToken"];
        ViewBag.Email = TempData["InviteEmail"];
        ViewBag.Message = TempData["InviteMessage"];
        return View();
    }

    [HttpGet]
    public IActionResult GettingStarted() => View();

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
