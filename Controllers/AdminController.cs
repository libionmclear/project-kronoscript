using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);
        var totalUsers = await _db.Users.CountAsync();
        var newUsersThisWeek = await _db.Users.CountAsync(u => u.CreatedAt >= sevenDaysAgo);

        // Active in last 30 days = users who posted in that period
        var activeUsersLast30Days = await _db.LifeEventPosts
            .Where(p => p.CreatedAt >= thirtyDaysAgo)
            .Select(p => p.OwnerUserId)
            .Distinct()
            .CountAsync();

        // Active now = posted in last 24 hours (no session tracking without LastActivityAt)
        var activeUsersNow = await _db.LifeEventPosts
            .Where(p => p.CreatedAt >= now.AddHours(-24))
            .Select(p => p.OwnerUserId)
            .Distinct()
            .CountAsync();

        var totalPosts = await _db.LifeEventPosts.CountAsync();
        var totalComments = await _db.Comments.CountAsync();
        var totalLikes = await _db.PostLikes.CountAsync();

        int activeBans = 0, permanentBans = 0;
        try
        {
            activeBans = await _db.UserBans.CountAsync(b =>
                b.BanType == BanType.Temporary &&
                (b.BanExpiry == null || b.BanExpiry > now));
            permanentBans = await _db.UserBans.CountAsync(b => b.BanType == BanType.Permanent);
        }
        catch { /* UserBans table may not exist yet */ }

        var vm = new AdminDashboardViewModel
        {
            TotalUsers = totalUsers,
            ActiveUsersLast30Days = activeUsersLast30Days,
            ActiveUsersNow = activeUsersNow,
            NewUsersThisWeek = newUsersThisWeek,
            TotalPosts = totalPosts,
            TotalComments = totalComments,
            TotalLikes = totalLikes,
            ActiveBans = activeBans,
            PermanentBans = permanentBans
        };

        return View(vm);
    }

    public async Task<IActionResult> Users(string? search = null)
    {
        var now = DateTime.UtcNow;

        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u =>
                (u.UserName != null && u.UserName.Contains(search)) ||
                (u.Email != null && u.Email.Contains(search)) ||
                (u.DisplayName != null && u.DisplayName.Contains(search)));

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var adminIds = adminUsers.Select(u => u.Id).ToHashSet();

        var postCounts = await _db.LifeEventPosts
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        Dictionary<string, UserBan> bansByUser = new();
        try
        {
            var activeBans = await _db.UserBans
                .Where(b => b.UserId != null &&
                            (b.BanType == BanType.Permanent ||
                             (b.BanExpiry != null && b.BanExpiry > now)))
                .ToListAsync();
            bansByUser = activeBans.ToDictionary(b => b.UserId!, b => b);
        }
        catch { /* UserBans table may not exist yet */ }

        var vms = users.Select(u => new AdminUserViewModel
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            Email = u.Email ?? "",
            DisplayName = u.DisplayName,
            FirstName = u.FirstName,
            LastName = u.LastName,
            CreatedAt = u.CreatedAt,
            PostCount = postCounts.TryGetValue(u.Id, out var pc) ? pc : 0,
            IsAdmin = adminIds.Contains(u.Id),
            ActiveBan = bansByUser.TryGetValue(u.Id, out var ban) ? ban : null
        }).ToList();

        ViewBag.Search = search;
        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BanUser(string userId, string? reason)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var adminId = _userManager.GetUserId(User);

        // Remove any existing temporary ban before adding a new one
        var existing = await _db.UserBans
            .Where(b => b.UserId == userId && b.BanType == BanType.Temporary)
            .ToListAsync();
        _db.UserBans.RemoveRange(existing);

        _db.UserBans.Add(new UserBan
        {
            UserId = userId,
            BannedEmail = user.Email!,
            BanType = BanType.Temporary,
            BannedAt = DateTime.UtcNow,
            BanExpiry = DateTime.UtcNow.AddDays(30),
            BannedByUserId = adminId,
            Reason = reason
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = $"User @{user.UserName} has been banned for 30 days.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnbanUser(int banId)
    {
        var ban = await _db.UserBans.FindAsync(banId);
        if (ban != null)
        {
            _db.UserBans.Remove(ban);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Ban removed.";
        }
        return RedirectToAction(nameof(Users));
    }

    // ── Tips & Announcements ──────────────────────────────────────────────

    public async Task<IActionResult> Tips()
    {
        List<Tip> tips;
        try { tips = await _db.Tips.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync(); }
        catch { tips = new List<Tip>(); }
        return View(tips);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTip(TipType type, string text, int sortOrder)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _db.Tips.Add(new Tip { Type = type, Text = text.Trim(), SortOrder = sortOrder, IsActive = true, CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Tip added.";
        }
        return RedirectToAction(nameof(Tips));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTip(int id, TipType type, string text, bool isActive, int sortOrder)
    {
        var tip = await _db.Tips.FindAsync(id);
        if (tip != null)
        {
            tip.Type = type;
            tip.Text = text.Trim();
            tip.IsActive = isActive;
            tip.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Tip updated.";
        }
        return RedirectToAction(nameof(Tips));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTip(int id)
    {
        var tip = await _db.Tips.FindAsync(id);
        if (tip != null) { _db.Tips.Remove(tip); await _db.SaveChangesAsync(); TempData["Success"] = "Tip deleted."; }
        return RedirectToAction(nameof(Tips));
    }

    // ── User Feed (admin view) ────────────────────────────────────────────

    public async Task<IActionResult> UserFeed(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var posts = await _db.LifeEventPosts
            .Include(p => p.Comments)
            .Include(p => p.Likes)
            .Include(p => p.Media)
            .Where(p => p.OwnerUserId == id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        ViewBag.ProfileUser = user;
        return View(posts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUser(string userId, string? reason)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        // Prevent removing another admin
        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Error"] = "Cannot remove an admin account.";
            return RedirectToAction(nameof(Users));
        }

        var adminId = _userManager.GetUserId(User);
        var email = user.Email!;

        // Add a permanent ban on the email before deleting
        // Remove any previous bans for this user first
        var existing = await _db.UserBans.Where(b => b.UserId == userId).ToListAsync();
        _db.UserBans.RemoveRange(existing);

        _db.UserBans.Add(new UserBan
        {
            UserId = null, // will be null after deletion
            BannedEmail = email,
            BanType = BanType.Permanent,
            BannedAt = DateTime.UtcNow,
            BanExpiry = null,
            BannedByUserId = adminId,
            Reason = reason
        });

        await _db.SaveChangesAsync();

        // Delete the user (cascades to posts, comments, likes)
        await _userManager.DeleteAsync(user);

        TempData["Success"] = $"User account ({email}) has been permanently removed and banned.";
        return RedirectToAction(nameof(Users));
    }
}
