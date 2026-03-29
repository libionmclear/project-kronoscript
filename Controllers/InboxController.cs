using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Controllers;

[Authorize]
public class InboxController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public InboxController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
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

        var vm = new InboxViewModel
        {
            Conversations = conversations,
            TotalUnread = conversations.Sum(c => c.UnreadCount)
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

        var vm = new MessageThreadViewModel
        {
            OtherUser = otherUser,
            Messages = messages,
            ComposeToId = id
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(string recipientId, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "Message cannot be empty.";
            return RedirectToAction("Conversation", new { id = recipientId });
        }

        var userId = _userManager.GetUserId(User)!;

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
        }
        catch
        {
            TempData["Error"] = "Could not send message.";
        }

        return RedirectToAction("Conversation", new { id = recipientId });
    }

    public async Task<IActionResult> Compose(string? to)
    {
        ApplicationUser? recipient = null;
        if (!string.IsNullOrEmpty(to))
            recipient = await _userManager.FindByIdAsync(to);

        var vm = new MessageThreadViewModel
        {
            OtherUser = recipient ?? new ApplicationUser(),
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
}
