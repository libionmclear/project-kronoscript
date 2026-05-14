using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Controllers;

/// <summary>
/// "Book mode" — every story the user has published, rendered as a
/// memoir: cover page, table of contents, stories grouped by decade
/// and year. Pure read view, no editing here.
///
/// Free for the owner (it's the moment they realise "this IS my
/// memoir" — the strongest emotional pull on this product). Drafts
/// and soft-deleted posts are excluded.
/// </summary>
[Authorize]
public class BookController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public BookController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        // The owner's published stories, chronological by event date.
        // We tolerate the EventMonth / EventDay being null (a year-only
        // post sorts to the start of its year). DeletedAt is filtered
        // by a global query filter already.
        var posts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == user.Id && !p.IsDraft)
            .Include(p => p.Media)
            .OrderBy(p => p.EventYear)
                .ThenBy(p => p.EventMonth ?? 0)
                .ThenBy(p => p.EventDay ?? 0)
                .ThenBy(p => p.CreatedAt)
            .ToListAsync();

        ViewBag.Author    = user;
        ViewBag.StoryCount = posts.Count;
        return View(posts);
    }
}
