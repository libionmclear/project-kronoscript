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

    // GET: /Notifications/Recent — JSON list of last 20 notifications for the bell dropdown
    [HttpGet]
    public async Task<IActionResult> Recent()
    {
        var userId = _userManager.GetUserId(User)!;
        var items = await _notifications.GetRecentAsync(userId, 20);
        var data = items.Select(n => new
        {
            id = n.Id,
            type = n.Type.ToString(),
            text = n.Text,
            link = n.LinkUrl ?? "#",
            unread = n.ReadAt == null,
            createdAt = n.CreatedAt,
            actorName = n.Actor?.DisplayName ?? n.Actor?.UserName,
            actorPhoto = n.Actor?.ProfilePhotoUrl
        });
        return Json(data);
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
