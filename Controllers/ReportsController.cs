using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notifications;

    public ReportsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
    }

    // POST: /Reports/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string targetType, string targetId, string? reason, string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(targetType) || string.IsNullOrEmpty(targetId))
            return Redirect(SafeReturn(returnUrl));
        if (!Enum.TryParse<ReportTargetType>(targetType, true, out var t))
            return Redirect(SafeReturn(returnUrl));

        var meId = _userManager.GetUserId(User)!;
        // Don't let users report themselves; rate-limit-ish: one pending report per (reporter, target).
        if (t == ReportTargetType.User && targetId == meId)
            return Redirect(SafeReturn(returnUrl));

        var existing = await _db.Reports
            .Where(r => r.ReporterUserId == meId && r.TargetType == t && r.TargetId == targetId && r.Status == ReportStatus.Pending)
            .FirstOrDefaultAsync();
        if (existing == null)
        {
            _db.Reports.Add(new Report
            {
                ReporterUserId = meId,
                TargetType = t,
                TargetId = targetId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Status = ReportStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            // Notify admins so they don't miss the queue.
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in admins)
            {
                await _notifications.CreateAsync(
                    admin.Id,
                    NotificationType.Announcement,
                    $"New {t.ToString().ToLower()} report",
                    "/Admin/Reports",
                    meId);
            }
        }

        TempData["Success"] = "Report sent. Admins will review it. Thank you.";
        return Redirect(SafeReturn(returnUrl));
    }

    private string SafeReturn(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return returnUrl;
        return Url.Action("Index", "Home") ?? "/";
    }
}
