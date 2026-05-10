using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Hubs;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class InboxController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFriendService _friendService;
    private readonly IHubContext<MessageHub> _messageHub;

    public InboxController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IFriendService friendService,
        IHubContext<MessageHub> messageHub)
    {
        _db = db;
        _userManager = userManager;
        _friendService = friendService;
        _messageHub = messageHub;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;

        List<Message> messages;
        try
        {
            messages = await _db.Messages
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .Where(m => m.SenderUserId == userId || m.RecipientUserId == userId)
                .OrderByDescending(m => m.SentAt)
                .ToListAsync();
        }
        catch
        {
            messages = new List<Message>();
        }

        // Group by the other participant, keep latest message per conversation
        var conversations = messages
            .GroupBy(m => m.SenderUserId == userId ? m.RecipientUserId : m.SenderUserId)
            .Select(g =>
            {
                var last = g.First();
                var otherUser = last.SenderUserId == userId ? last.Recipient : last.Sender;
                return new ConversationSummaryViewModel
                {
                    OtherUser = otherUser,
                    LastMessage = last,
                    UnreadCount = g.Count(m => m.RecipientUserId == userId && !m.IsRead)
                };
            })
            .OrderByDescending(c => c.LastMessage.SentAt)
            .ToList();

        // Load contacts grouped by tier
        var friendList = await _friendService.GetFriendListAsync(userId);

        var vm = new InboxViewModel
        {
            Conversations = conversations,
            TotalUnread = conversations.Sum(c => c.UnreadCount),
            Family = friendList.Friends
                .Where(f => f.Tier == FriendTier.Family)
                .Select(f => new InboxContactViewModel { User = f.User })
                .Concat(friendList.RelativeFamily.Select(r => new InboxContactViewModel { User = r.User }))
                .ToList(),
            Friends = friendList.Friends
                .Where(f => f.Tier == FriendTier.Friend)
                .Select(f => new InboxContactViewModel { User = f.User })
                .ToList(),
            Acquaintances = friendList.Friends
                .Where(f => f.Tier == FriendTier.Acquaintance)
                .Select(f => new InboxContactViewModel { User = f.User })
                .ToList(),
        };

        return View(vm);
    }

    public async Task<IActionResult> Conversation(string id)
    {
        var userId = _userManager.GetUserId(User)!;
        var otherUser = await _userManager.FindByIdAsync(id);
        if (otherUser == null) return NotFound();

        List<Message> messages;
        try
        {
            messages = await _db.Messages
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .Where(m =>
                    (m.SenderUserId == userId && m.RecipientUserId == id) ||
                    (m.SenderUserId == id && m.RecipientUserId == userId))
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            // Mark unread as read
            var unread = messages.Where(m => m.RecipientUserId == userId && !m.IsRead).ToList();
            foreach (var msg in unread) msg.IsRead = true;
            if (unread.Any()) await _db.SaveChangesAsync();
        }
        catch
        {
            messages = new List<Message>();
        }

        var currentUser = await _userManager.FindByIdAsync(userId);
        var vm = new MessageThreadViewModel
        {
            OtherUser = otherUser,
            CurrentUser = currentUser,
            Messages = messages,
            ComposeToId = id
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> Send(string recipientId, string body)
    {
        // The conversation view sends with X-Requested-With: XMLHttpRequest
        // (or accepts JSON) so we can return the saved message and append it
        // immediately, instead of redirect-and-reload. Plain form posts
        // still get the old redirect behavior as a fallback.
        var wantsJson = Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                        || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(recipientId))
        {
            if (wantsJson) return BadRequest(new { error = "Please select a recipient." });
            TempData["ChatError"] = "Please select a recipient.";
            return RedirectToAction("Compose");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            if (wantsJson) return BadRequest(new { error = "Message cannot be empty." });
            TempData["ChatError"] = "Message cannot be empty.";
            return RedirectToAction("Conversation", new { id = recipientId });
        }

        var userId = _userManager.GetUserId(User)!;
        Message? saved = null;
        string? error = null;

        try
        {
            var msg = new Message
            {
                SenderUserId = userId,
                RecipientUserId = recipientId,
                Body = body.Trim(),
                SentAt = DateTime.UtcNow,
                IsRead = false
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();
            saved = msg;
        }
        catch (Exception ex)
        {
            error = $"Could not send message: {ex.Message}";
        }

        // Push to the recipient's other open tabs *and* the sender's other
        // tabs so a message typed on phone appears on desktop instantly.
        if (saved != null)
        {
            var sender = await _userManager.FindByIdAsync(userId);
            var payload = new
            {
                id = saved.Id,
                body = saved.Body,
                sentAt = saved.SentAt,
                senderId = saved.SenderUserId,
                recipientId = saved.RecipientUserId,
                senderDisplayName = sender?.DisplayName ?? sender?.UserName,
                senderProfilePhotoUrl = sender?.ProfilePhotoUrl
            };
            try
            {
                await _messageHub.Clients.Group(saved.RecipientUserId).SendAsync("messageReceived", payload);
                await _messageHub.Clients.Group(saved.SenderUserId).SendAsync("messageReceived", payload);
            }
            catch
            {
                // If the hub is briefly unavailable, the message is still
                // saved — clients will pick it up on next page load.
            }
        }

        if (wantsJson)
        {
            if (error != null) return StatusCode(500, new { error });
            return Json(new
            {
                id = saved!.Id,
                body = saved.Body,
                sentAt = saved.SentAt,
                senderId = saved.SenderUserId,
                recipientId = saved.RecipientUserId
            });
        }

        if (error != null) TempData["ChatError"] = error;
        return RedirectToAction("Conversation", new { id = recipientId });
    }

    public async Task<IActionResult> Compose(string? to)
    {
        var userId = _userManager.GetUserId(User)!;
        ApplicationUser? recipient = null;
        if (!string.IsNullOrEmpty(to))
            recipient = await _userManager.FindByIdAsync(to);

        var currentUser = await _userManager.FindByIdAsync(userId);
        var vm = new MessageThreadViewModel
        {
            OtherUser = recipient ?? new ApplicationUser(),
            CurrentUser = currentUser,
            Messages = new List<Message>(),
            ComposeToId = to
        };
        return View("Conversation", vm);
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = _userManager.GetUserId(User)!;
        try
        {
            var count = await _db.Messages.CountAsync(m => m.RecipientUserId == userId && !m.IsRead);
            return Json(count);
        }
        catch
        {
            return Json(0);
        }
    }

    // GET: /Inbox/Thread/{id} — JSON payload for the chat-dock's inline
    // thread view: the other user's display info + the most recent N
    // messages with this person, marking any unread ones as read in the
    // same call (mirrors the full Conversation page behavior).
    [HttpGet]
    public async Task<IActionResult> Thread(string id, int take = 50)
    {
        var userId = _userManager.GetUserId(User)!;
        var otherUser = await _userManager.FindByIdAsync(id);
        if (otherUser == null) return NotFound();

        try
        {
            var messages = await _db.Messages
                .Where(m =>
                    (m.SenderUserId == userId && m.RecipientUserId == id) ||
                    (m.SenderUserId == id && m.RecipientUserId == userId))
                .OrderByDescending(m => m.SentAt)
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync();
            messages.Reverse(); // oldest first for rendering

            var unread = messages.Where(m => m.RecipientUserId == userId && !m.IsRead).ToList();
            if (unread.Count > 0)
            {
                foreach (var m in unread) m.IsRead = true;
                await _db.SaveChangesAsync();
            }

            string Initials()
            {
                if (!string.IsNullOrEmpty(otherUser.FirstName) && !string.IsNullOrEmpty(otherUser.LastName))
                    return $"{otherUser.FirstName[0]}{otherUser.LastName[0]}".ToUpper();
                var un = otherUser.DisplayName ?? otherUser.UserName ?? "?";
                return (un.Length >= 2 ? un[..2] : un).ToUpper();
            }

            return Json(new
            {
                otherId = otherUser.Id,
                otherName = otherUser.DisplayName ?? otherUser.UserName,
                otherUsername = otherUser.UserName,
                otherPhoto = otherUser.ProfilePhotoUrl,
                otherInitials = Initials(),
                messages = messages.Select(m => new
                {
                    id = m.Id,
                    body = m.Body,
                    sentAt = m.SentAt,
                    senderId = m.SenderUserId,
                    recipientId = m.RecipientUserId
                }).ToList()
            });
        }
        catch
        {
            return Json(new { otherId = id, otherName = otherUser.DisplayName ?? otherUser.UserName, otherUsername = otherUser.UserName, otherPhoto = otherUser.ProfilePhotoUrl, otherInitials = "?", messages = Array.Empty<object>() });
        }
    }

    // GET: /Inbox/DockData — JSON feed for the floating chat-dock UI in the
    // layout. Returns the user's recent conversations (latest message,
    // unread count, the other user's name + avatar) and a flag indicating
    // whether kronoadmin (the feedback recipient) is reachable.
    [HttpGet]
    public async Task<IActionResult> DockData()
    {
        var userId = _userManager.GetUserId(User)!;
        try
        {
            var messages = await _db.Messages
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .Where(m => m.SenderUserId == userId || m.RecipientUserId == userId)
                .OrderByDescending(m => m.SentAt)
                .Take(200)
                .ToListAsync();

            var convs = messages
                .GroupBy(m => m.SenderUserId == userId ? m.RecipientUserId : m.SenderUserId)
                .Select(g =>
                {
                    var last = g.First();
                    var other = last.SenderUserId == userId ? last.Recipient : last.Sender;
                    var preview = (last.Body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                    if (preview.Length > 80) preview = preview[..80].TrimEnd() + "…";
                    return new
                    {
                        otherId = other.Id,
                        otherName = other.DisplayName ?? other.UserName,
                        otherUsername = other.UserName,
                        otherPhoto = other.ProfilePhotoUrl,
                        lastBody = preview,
                        lastFromMe = last.SenderUserId == userId,
                        sentAt = last.SentAt,
                        unread = g.Count(m => m.RecipientUserId == userId && !m.IsRead)
                    };
                })
                .OrderByDescending(c => c.sentAt)
                .Take(15)
                .ToList();

            var admin = await _userManager.FindByNameAsync("kronoadmin");
            return Json(new
            {
                conversations = convs,
                totalUnread = convs.Sum(c => c.unread),
                adminAvailable = admin != null,
                adminId = admin?.Id
            });
        }
        catch
        {
            return Json(new { conversations = Array.Empty<object>(), totalUnread = 0, adminAvailable = false, adminId = (string?)null });
        }
    }
}
