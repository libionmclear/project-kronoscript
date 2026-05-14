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
        //
        // Channel posts and posts written *as* a biographical user are
        // excluded — those belong to their forum (the channel, or the
        // biographical user's own memoir) and aren't part of the
        // author's personal life story even though Marco's account
        // typed them. Editing happens in the original forum, not here.
        var posts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == user.Id
                        && !p.IsDraft
                        && p.ChannelId == null
                        && (p.Owner == null || !p.Owner.IsBiographical))
            .Include(p => p.Media)
            .OrderBy(p => p.EventYear)
                .ThenBy(p => p.EventMonth ?? 0)
                .ThenBy(p => p.EventDay ?? 0)
                .ThenBy(p => p.CreatedAt)
            .ToListAsync();

        var chapters = await _db.BookChapters
            .Where(c => c.OwnerUserId == user.Id)
            .OrderBy(c => c.Year).ThenBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync();

        ViewBag.Author     = user;
        ViewBag.StoryCount = posts.Count;
        ViewBag.Chapters   = chapters;
        return View(posts);
    }

    /// <summary>Owner toggles "finalised" on one of their posts from
    /// the book view. Pure curation — no visibility side-effects.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFinalised(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post == null) return NotFound();
        if (post.OwnerUserId != userId) return Forbid();
        post.IsFinalised = !post.IsFinalised;
        await _db.SaveChangesAsync();
        // Send the reader back to the same page in the book.
        return RedirectToAction(nameof(Index), null, $"story-{post.EventYear}-{post.Id}");
    }

    // ── /Book/Organize — the editor view ──────────────────────────
    //
    // Per-year rows. Each row shows: that year's BookChapters (cards
    // with their stories nested as bubbles) plus an "Unassigned" zone
    // for stories the user hasn't placed yet. Forms let the user add
    // a chapter, rename it, delete it, or move a story between
    // chapters within its year.
    public async Task<IActionResult> Organize()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var posts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == user.Id
                        && !p.IsDraft
                        && p.ChannelId == null
                        && (p.Owner == null || !p.Owner.IsBiographical))
            .Include(p => p.Media)
            .OrderBy(p => p.EventYear)
                .ThenBy(p => p.EventMonth ?? 0)
                .ThenBy(p => p.EventDay ?? 0)
                .ThenBy(p => p.CreatedAt)
            .ToListAsync();

        var chapters = await _db.BookChapters
            .Where(c => c.OwnerUserId == user.Id)
            .OrderBy(c => c.Year).ThenBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync();

        ViewBag.Author     = user;
        ViewBag.Chapters   = chapters;
        ViewBag.StoryCount = posts.Count;
        return View(posts);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateChapter(int year, string title)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (year < 1 || year > 2100)
        {
            TempData["Error"] = "Year must be between 1 and 2100.";
            return RedirectToAction(nameof(Organize));
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Chapter title is required.";
            return RedirectToAction(nameof(Organize));
        }
        var existingMax = await _db.BookChapters
            .Where(c => c.OwnerUserId == userId && c.Year == year)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync() ?? -1;
        _db.BookChapters.Add(new BookChapter
        {
            OwnerUserId = userId,
            Year        = year,
            Title       = title.Trim(),
            SortOrder   = existingMax + 1,
            CreatedAt   = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Organize), null, $"year-{year}");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameChapter(int id, string title)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var chapter = await _db.BookChapters.FirstOrDefaultAsync(c => c.Id == id);
        if (chapter == null) return NotFound();
        if (chapter.OwnerUserId != userId) return Forbid();
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Chapter title can't be empty.";
            return RedirectToAction(nameof(Organize));
        }
        chapter.Title = title.Trim();
        chapter.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Organize), null, $"year-{chapter.Year}");
    }

    /// <summary>Removes a chapter. Stories it held aren't deleted —
    /// their BookChapterId is set to null so they fall back to the
    /// "Unassigned" zone for the year.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChapter(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var chapter = await _db.BookChapters.FirstOrDefaultAsync(c => c.Id == id);
        if (chapter == null) return NotFound();
        if (chapter.OwnerUserId != userId) return Forbid();
        // Unassign any stories in this chapter before removing it.
        var stories = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId && p.BookChapterId == id)
            .ToListAsync();
        foreach (var s in stories) s.BookChapterId = null;
        _db.BookChapters.Remove(chapter);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Organize), null, $"year-{chapter.Year}");
    }

    /// <summary>Move a story into a chapter (or "Unassigned" by
    /// passing chapterId = null/0). Both must belong to the same year
    /// and to the caller — we don't allow cross-year regrouping.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignStory(int postId, int? chapterId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null) return NotFound();
        if (post.OwnerUserId != userId) return Forbid();
        if (chapterId.HasValue && chapterId.Value > 0)
        {
            var chapter = await _db.BookChapters
                .FirstOrDefaultAsync(c => c.Id == chapterId.Value);
            if (chapter == null) return NotFound();
            if (chapter.OwnerUserId != userId) return Forbid();
            if (chapter.Year != post.EventYear)
            {
                TempData["Error"] = "A story can only join a chapter of the same year.";
                return RedirectToAction(nameof(Organize), null, $"year-{post.EventYear}");
            }
            post.BookChapterId = chapter.Id;
        }
        else
        {
            post.BookChapterId = null;
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Organize), null, $"year-{post.EventYear}");
    }
}
