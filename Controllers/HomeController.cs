using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
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
    private readonly IEmailSender _emailSender;

    public HomeController(
        ILogger<HomeController> logger,
        UserManager<ApplicationUser> userManager,
        IFriendService friendService,
        IPostService postService,
        ApplicationDbContext db,
        IEmailSender emailSender)
    {
        _logger = logger;
        _userManager = userManager;
        _friendService = friendService;
        _postService = postService;
        _db = db;
        _emailSender = emailSender;
    }

    public async Task<IActionResult> Index(string? prompt = null)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
            ViewBag.PromptHint = prompt.Trim();

        if (!User.Identity!.IsAuthenticated)
        {
            try
            {
                ViewBag.QuillMessages = await _db.QuillMessages
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .ThenBy(m => m.Id)
                    .Select(m => m.Text)
                    .ToListAsync();
            }
            catch
            {
                ViewBag.QuillMessages = new List<string>();
            }
            return View();
        }

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

        // Recent feed: own posts + friends posts mixed (drafts excluded)
        var ownPosts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId && !p.IsDraft)
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
                            && !p.IsDraft
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
    public async Task<IActionResult> Invite()
    {
        var me = await _userManager.GetUserAsync(User);
        var fullName = (string.IsNullOrWhiteSpace(me?.FirstName) && string.IsNullOrWhiteSpace(me?.LastName))
            ? (me?.DisplayName ?? me?.UserName ?? "A friend")
            : ($"{me?.FirstName} {me?.LastName}").Trim();

        return View(new InviteViewModel
        {
            Subject = $"{fullName} invites you to Kronoscript",
            Mode = "send"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var me = await _userManager.GetUserAsync(User);
        var fullName = (string.IsNullOrWhiteSpace(me?.FirstName) && string.IsNullOrWhiteSpace(me?.LastName))
            ? (me?.DisplayName ?? me?.UserName ?? "A friend")
            : ($"{me?.FirstName} {me?.LastName}").Trim();
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

        var inviteUrl = $"{Request.Scheme}://{Request.Host}/Account/Register?invite={token}";

        if (string.Equals(model.Mode, "send", StringComparison.OrdinalIgnoreCase))
        {
            var subject = string.IsNullOrWhiteSpace(model.Subject)
                ? $"{fullName} invites you to Kronoscript"
                : model.Subject.Trim();

            var personalNote = System.Net.WebUtility.HtmlEncode(model.Message ?? "").Replace("\n", "<br/>");
            var html =
                $"<div style=\"font-family:'Segoe UI',Arial,sans-serif;color:#1a1f2e;max-width:560px;margin:0 auto;\">" +
                $"<h2 style=\"font-family:Georgia,serif;color:#0f6466;\">You've been invited to Kronoscript</h2>" +
                $"<p><strong>{System.Net.WebUtility.HtmlEncode(fullName)}</strong> wants to write your shared story together.</p>" +
                $"<blockquote style=\"border-left:3px solid #b8871a;padding:8px 14px;color:#444;background:#fafaf7;\">{personalNote}</blockquote>" +
                $"<p style=\"text-align:center;margin:28px 0;\">" +
                $"<a href=\"{inviteUrl}\" style=\"background:#0f6466;color:#fff;padding:12px 28px;border-radius:10px;text-decoration:none;font-weight:600;\">Accept &amp; Join Kronoscript</a>" +
                $"</p>" +
                $"<p style=\"font-size:0.85rem;color:#6b7280;\">Or paste this link in your browser:<br/><a href=\"{inviteUrl}\">{inviteUrl}</a></p>" +
                $"<hr style=\"border:none;border-top:1px solid #e6e6df;margin:24px 0;\"/>" +
                $"<p style=\"font-size:0.78rem;color:#9ca3af;\">Kronoscript &mdash; Live it today. Remember it forever. Tell it together.</p>" +
                $"</div>";

            try
            {
                await _emailSender.SendEmailAsync(model.Email, subject, html);
                TempData["Success"] = $"Invite sent to {model.Email}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invite email failed for {Email}", model.Email);
                TempData["Error"] = "Could not send the email. Try the shareable link option instead.";
                TempData["InviteToken"] = token;
                TempData["InviteEmail"] = model.Email;
                TempData["InviteMessage"] = model.Message;
                return RedirectToAction("InviteSent");
            }

            return RedirectToAction("Invite");
        }

        // Mode: link (just generate the shareable link page)
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
