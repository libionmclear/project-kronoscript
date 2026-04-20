using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class SearchController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;

    public SearchController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IPermissionService permissionService)
    {
        _db = db;
        _userManager = userManager;
        _permissionService = permissionService;
    }

    private static readonly (string Name, string Url, string Hint)[] FEATURES = new[]
    {
        ("Home feed",          "/",                          "Your daily feed of stories"),
        ("My Timeline",        "/Posts/Timeline",            "Your chronological life story"),
        ("My Profile",         "/Profile",                   "View and edit your profile"),
        ("Edit Profile",       "/Profile/Edit",              "Photo, bio, nationalities, privacy"),
        ("Working Index",      "/WorkingIndex",              "Private year-by-year life grid"),
        ("Friends & Network",  "/Friends",                   "Manage friends, family, acquaintances"),
        ("Messages",           "/Inbox",                     "Private chats with your network"),
        ("Tagged in",          "/Posts/Tagged",              "Stories where someone tagged you"),
        ("Export My Story",    "/Export",                    "Download your story as a document"),
        ("Invite a Friend",    "/Home/Invite",               "Send an invite by email"),
        ("Getting Started",    "/Home/GettingStarted",       "How Kronoscript works"),
        ("New Full Story",     "/Posts/Create",              "Open the full story editor"),
        ("Change Password",    "/Profile/ChangePassword",    "Update your password")
    };

    [HttpGet]
    public async Task<IActionResult> Query(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2)
            return Json(new { people = Array.Empty<object>(), posts = Array.Empty<object>(), features = Array.Empty<object>() });

        var userId = _userManager.GetUserId(User)!;
        var qLower = q.ToLower();

        // People (users by display name / username / first / last) — exclude self
        var people = await _db.Users
            .Where(u => u.Id != userId)
            .Where(u =>
                (u.DisplayName != null && u.DisplayName.ToLower().Contains(qLower)) ||
                (u.UserName    != null && u.UserName.ToLower().Contains(qLower)) ||
                (u.FirstName   != null && u.FirstName.ToLower().Contains(qLower)) ||
                (u.LastName    != null && u.LastName.ToLower().Contains(qLower)))
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .Take(8)
            .Select(u => new
            {
                id = u.Id,
                name = u.DisplayName ?? u.UserName,
                userName = u.UserName,
                photo = u.ProfilePhotoUrl,
                url = "/Profile/Index/" + u.Id
            })
            .ToListAsync();

        // Posts (title/body) — start broad then filter by viewer access
        var rawPosts = await _db.LifeEventPosts
            .Where(p => !p.IsDraft || p.OwnerUserId == userId)
            .Where(p =>
                (p.Title != null && p.Title.ToLower().Contains(qLower)) ||
                (p.Body != null  && p.Body.ToLower().Contains(qLower)))
            .Include(p => p.Owner)
            .OrderByDescending(p => p.CreatedAt)
            .Take(40)
            .ToListAsync();

        var allowedPosts = new List<object>();
        foreach (var p in rawPosts)
        {
            if (allowedPosts.Count >= 8) break;
            var canSee = p.OwnerUserId == userId
                ? true
                : await _permissionService.CanViewPostsAsync(userId, p.OwnerUserId)
                  && p.Visibility != PostVisibility.Private;
            // Quick visibility check (broad — full per-tier check happens on the post page)
            if (!canSee) continue;
            var snippet = MakeSnippet(p.Body, qLower);
            allowedPosts.Add(new
            {
                id = p.Id,
                title = p.Title ?? "Life event",
                snippet,
                year = p.EventYear,
                authorName = p.Owner?.DisplayName ?? p.Owner?.UserName ?? "",
                url = "/Posts/Detail/" + p.Id
            });
        }

        // Features (static list)
        var features = FEATURES
            .Where(f => f.Name.ToLower().Contains(qLower) || f.Hint.ToLower().Contains(qLower))
            .Take(6)
            .Select(f => new { name = f.Name, hint = f.Hint, url = f.Url })
            .ToList();

        return Json(new { people, posts = allowedPosts, features });
    }

    private static string MakeSnippet(string? body, string qLower, int radius = 50)
    {
        if (string.IsNullOrEmpty(body)) return "";
        var idx = body.ToLower().IndexOf(qLower, StringComparison.Ordinal);
        if (idx < 0) return body.Length > 100 ? body[..100] + "…" : body;
        var start = Math.Max(0, idx - radius);
        var len = Math.Min(body.Length - start, qLower.Length + radius * 2);
        var snip = body.Substring(start, len);
        if (start > 0) snip = "…" + snip;
        if (start + len < body.Length) snip += "…";
        return snip;
    }
}
