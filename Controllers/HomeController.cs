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
            .Include(p => p.Comments)
            .Include(p => p.Likes)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .ToListAsync();

        var friendPosts = await _postService.GetFeedPostsAsync(userId);

        var allPosts = ownPosts
            .Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId)
            })
            .Concat(friendPosts.Take(15).Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId)
            }))
            .OrderByDescending(p => p.Post.CreatedAt)
            .Take(15)
            .ToList();

        var vm = new DashboardViewModel
        {
            RecentPosts = allPosts,
            FriendsCount = friendsCount,
            AcquaintancesCount = acquaintancesCount,
            FamilyCount = familyCount,
            TaggedCount = taggedCount,
            ActiveFriends = activeFriends
        };

        return View(vm);
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
