using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationsController(INotificationService notifications, UserManager<ApplicationUser> userManager)
    {
        _notifications = notifications;
        _userManager = userManager;
    }

    // GET: /Notifications — full-page list (replaces the navbar dropdown).
    // Pulls more rows than the dropdown did and groups them by (type, link)
    // so a noisy thread reads as "Alice and 3 others commented on your story"
    // instead of repeated rows. Marks everything read on view.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var raw = await _notifications.GetRecentAsync(userId, 200);

        var grouped = raw
            .GroupBy(n => new { n.Type, Link = n.LinkUrl ?? "" })
            .Select(g =>
            {
                var ordered = g.OrderByDescending(n => n.CreatedAt).ToList();
                var head = ordered.First();
                var more = ordered.Count - 1;
                return new MyStoryTold.Models.ViewModels.NotificationGroupViewModel
                {
                    Id = head.Id,
                    Type = head.Type.ToString(),
                    Text = more > 0 ? $"{head.Text} (+{more} more)" : head.Text,
                    Link = head.LinkUrl ?? "#",
                    Unread = ordered.Any(n => n.ReadAt == null),
                    CreatedAt = head.CreatedAt,
                    ActorName = head.Actor?.DisplayName ?? head.Actor?.UserName,
                    ActorPhoto = head.Actor?.ProfilePhotoUrl,
                    GroupCount = ordered.Count
                };
            })
            .OrderByDescending(x => x.CreatedAt)
            .Take(80)
            .ToList();

        // Drop the unread badge — visiting the page IS the "I saw them" gesture.
        await _notifications.MarkAllReadAsync(userId);

        return View(grouped);
    }

    // GET: /Notifications/Recent — JSON list of last ~20 notifications, grouped
    // when several events of the same type point at the same target so users
    // see "Alice and 2 others commented on your story X" instead of three rows.
    [HttpGet]
    public async Task<IActionResult> Recent()
    {
        var userId = _userManager.GetUserId(User)!;
        // Pull more than we'll display so grouping has material to collapse.
        var raw = await _notifications.GetRecentAsync(userId, 60);

        var groups = raw
            .GroupBy(n => new { n.Type, Link = n.LinkUrl ?? "" })
            .Select(g =>
            {
                var ordered = g.OrderByDescending(n => n.CreatedAt).ToList();
                var head = ordered.First();
                var more = ordered.Count - 1;
                var text = more > 0
                    ? $"{head.Text} (+{more} more)"
                    : head.Text;
                return new
                {
                    id = head.Id,
                    type = head.Type.ToString(),
                    text,
                    link = head.LinkUrl ?? "#",
                    unread = ordered.Any(n => n.ReadAt == null),
                    createdAt = head.CreatedAt,
                    actorName = head.Actor?.DisplayName ?? head.Actor?.UserName,
                    actorPhoto = head.Actor?.ProfilePhotoUrl,
                    groupCount = ordered.Count
                };
            })
            .OrderByDescending(x => x.createdAt)
            .Take(20)
            .ToList();

        return Json(groups);
    }

    // GET: /Notifications/UnreadCount — small JSON for the badge poller
    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = _userManager.GetUserId(User)!;
        return Json(await _notifications.GetUnreadCountAsync(userId));
    }

    // POST: /Notifications/MarkAllRead — called when the dropdown is opened
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _userManager.GetUserId(User)!;
        await _notifications.MarkAllReadAsync(userId);
        return Ok();
    }
}
