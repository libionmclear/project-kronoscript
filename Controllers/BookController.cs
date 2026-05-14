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
                        && p.IncludeInBook
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

    /// <summary>Owner toggles whether a post appears in the memoir
    /// Book view. Default true; user can flip it off to hide a story
    /// that the automatic channel/biographical filter didn't catch.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleIncludeInBook(int id, string? returnTo)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post == null) return NotFound();
        if (post.OwnerUserId != userId) return Forbid();
        post.IncludeInBook = !post.IncludeInBook;
        await _db.SaveChangesAsync();
        // "organize" returns to the Organize page so the user can keep
        // toggling without leaving the editor. Otherwise back to book.
        if (returnTo == "organize")
        {
            return RedirectToAction(nameof(Organize));
        }
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

        // Note: Organize INCLUDES hidden-from-book posts on purpose,
        // so the user can flip them back on. The Book reading view
        // (Index) is the one that drops them.
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
    public async Task<IActionResult> CreateChapter(int year, string title, int? parentChapterId)
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

        // If a parent is requested, validate ownership and prevent
        // sub-sub-chapters (one level deep is the sweet spot).
        int? parentId = null;
        if (parentChapterId.HasValue && parentChapterId.Value > 0)
        {
            var parent = await _db.BookChapters.FirstOrDefaultAsync(c => c.Id == parentChapterId.Value);
            if (parent == null) return NotFound();
            if (parent.OwnerUserId != userId) return Forbid();
            if (parent.ParentChapterId.HasValue)
            {
                TempData["Error"] = "Subchapters can't nest further.";
                return RedirectToAction(nameof(Organize));
            }
            parentId = parent.Id;
        }

        var existingMax = await _db.BookChapters
            .Where(c => c.OwnerUserId == userId
                        && c.Year == year
                        && c.ParentChapterId == parentId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync() ?? -1;
        _db.BookChapters.Add(new BookChapter
        {
            OwnerUserId     = userId,
            Year            = year,
            Title           = title.Trim(),
            SortOrder       = existingMax + 1,
            ParentChapterId = parentId,
            CreatedAt       = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Organize));
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
    /// passing chapterId = null/0). Chapters are editorial groupings,
    /// so cross-year membership is allowed — a chapter from 1989 can
    /// hold a story dated 1988 if the user wants it that way.</summary>
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
            post.BookChapterId = chapter.Id;
        }
        else
        {
            post.BookChapterId = null;
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Organize));
    }

    /// <summary>JSON-friendly mirror of AssignStory for the drag-drop
    /// flow on /Book/Organize. The client moves the bubble visually
    /// via Sortable.js and POSTs here to persist; on failure it can
    /// reload to recover.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignStoryAjax(int postId, int? chapterId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var post = await _db.LifeEventPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null) return Json(new { ok = false, error = "not found" });
        if (post.OwnerUserId != userId) return Json(new { ok = false, error = "forbidden" });
        if (chapterId.HasValue && chapterId.Value > 0)
        {
            var chapter = await _db.BookChapters
                .FirstOrDefaultAsync(c => c.Id == chapterId.Value);
            if (chapter == null) return Json(new { ok = false, error = "chapter not found" });
            if (chapter.OwnerUserId != userId) return Json(new { ok = false, error = "forbidden" });
            post.BookChapterId = chapter.Id;
        }
        else
        {
            post.BookChapterId = null;
        }
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    /// <summary>One-click: create one chapter per year the user has
    /// stories in, named with the year itself, and assign every
    /// currently-unassigned story from that year to it. Idempotent —
    /// years that already have at least one chapter are skipped so
    /// re-running doesn't duplicate.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoCreateYearChapters()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        // Years the user has eligible (non-channel, non-bio, non-draft)
        // stories in, sorted oldest first.
        var storyYears = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId
                        && !p.IsDraft
                        && p.ChannelId == null
                        && (p.Owner == null || !p.Owner.IsBiographical))
            .Select(p => p.EventYear)
            .Distinct()
            .OrderBy(y => y)
            .ToListAsync();

        var existingChapterYears = (await _db.BookChapters
            .Where(c => c.OwnerUserId == userId)
            .Select(c => c.Year)
            .Distinct()
            .ToListAsync()).ToHashSet();

        var created = 0;
        var assigned = 0;
        var now = DateTime.UtcNow;
        foreach (var year in storyYears)
        {
            if (existingChapterYears.Contains(year)) continue;
            var chapter = new BookChapter
            {
                OwnerUserId = userId,
                Year        = year,
                Title       = year.ToString(),
                SortOrder   = 0,
                CreatedAt   = now
            };
            _db.BookChapters.Add(chapter);
            await _db.SaveChangesAsync();
            created++;

            // Move every unassigned story from this year into the new chapter.
            var toAssign = await _db.LifeEventPosts
                .Where(p => p.OwnerUserId == userId
                            && !p.IsDraft
                            && p.ChannelId == null
                            && p.EventYear == year
                            && p.BookChapterId == null
                            && (p.Owner == null || !p.Owner.IsBiographical))
                .ToListAsync();
            foreach (var post in toAssign)
            {
                post.BookChapterId = chapter.Id;
                assigned++;
            }
            await _db.SaveChangesAsync();
        }

        if (created == 0)
        {
            TempData["Success"] = "Every year already has at least one chapter — nothing to do.";
        }
        else
        {
            TempData["Success"] = $"Created {created} year chapter{(created == 1 ? "" : "s")} and assigned {assigned} stor{(assigned == 1 ? "y" : "ies")}.";
        }
        return RedirectToAction(nameof(Organize));
    }

    /// <summary>Move a chapter under a new parent (or to top level by
    /// passing parentChapterId = null/0). One level deep — you can't
    /// nest a subchapter under another subchapter.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveChapter(int chapterId, int? parentChapterId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var chapter = await _db.BookChapters.FirstOrDefaultAsync(c => c.Id == chapterId);
        if (chapter == null) return Json(new { ok = false, error = "not found" });
        if (chapter.OwnerUserId != userId) return Json(new { ok = false, error = "forbidden" });

        // A chapter with sub-chapters can't itself become a sub-chapter.
        var hasChildren = await _db.BookChapters.AnyAsync(c => c.ParentChapterId == chapter.Id);
        if (parentChapterId.HasValue && parentChapterId.Value > 0)
        {
            if (hasChildren) return Json(new { ok = false, error = "parent_has_children" });
            var parent = await _db.BookChapters.FirstOrDefaultAsync(c => c.Id == parentChapterId.Value);
            if (parent == null) return Json(new { ok = false, error = "parent not found" });
            if (parent.OwnerUserId != userId) return Json(new { ok = false, error = "forbidden" });
            if (parent.ParentChapterId.HasValue) return Json(new { ok = false, error = "depth_limit" });
            if (parent.Id == chapter.Id) return Json(new { ok = false, error = "self" });
            chapter.ParentChapterId = parent.Id;
        }
        else
        {
            chapter.ParentChapterId = null;
        }
        chapter.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }
}
