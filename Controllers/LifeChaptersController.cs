using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// CRUD + visualization for "Life Chapters" — coarse-grained contexts
/// the user lived through (a job, a school, a parish, a hobby group).
/// Each chapter has a year range and a set of PersonProfile members.
/// The Map action renders the bubble timeline with avatars inside.
///
/// Same premium gate as People Profiles — chapters wrap people, so it
/// shouldn't be available to users who can't create profiles.
/// </summary>
[Authorize]
public class LifeChaptersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPremiumService _premium;

    public LifeChaptersController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IPremiumService premium)
    {
        _db = db;
        _userManager = userManager;
        _premium = premium;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            TempData["Info"] = "Eras aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var chapters = await _db.LifeChapters
            .Where(c => c.OwnerUserId == userId)
            .Include(c => c.Members).ThenInclude(m => m.PersonProfile)
            .OrderBy(c => c.StartYear)
                .ThenBy(c => c.Name)
            .ToListAsync();
        return View(chapters);
    }

    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            return RedirectToAction(nameof(Index));
        }
        await LoadProfilePickerAsync();
        return View(new LifeChapter { StartYear = DateTime.UtcNow.Year });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LifeChapter model, int[]? memberProfileIds)
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles)) return Forbid();

        ModelState.Remove(nameof(model.OwnerUserId));
        ModelState.Remove(nameof(model.Owner));
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A name is required.");
        }
        if (model.StartYear < 1 || model.StartYear > 2100)
        {
            ModelState.AddModelError(nameof(model.StartYear), "Start year must be between 1 and 2100.");
        }
        if (model.EndYear.HasValue && model.EndYear < model.StartYear)
        {
            ModelState.AddModelError(nameof(model.EndYear), "End year can't be before start year.");
        }
        if (!ModelState.IsValid)
        {
            await LoadProfilePickerAsync();
            return View(model);
        }

        model.OwnerUserId = userId;
        model.Name        = model.Name.Trim();
        model.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        model.Color       = string.IsNullOrWhiteSpace(model.Color)       ? null : model.Color.Trim();
        model.CreatedAt   = DateTime.UtcNow;
        model.UpdatedAt   = null;

        _db.LifeChapters.Add(model);
        await _db.SaveChangesAsync();

        if (memberProfileIds is { Length: > 0 })
        {
            await AddMembersAsync(model.Id, memberProfileIds, userId);
        }

        TempData["Success"] = $"Era \"{model.Name}\" created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var chapter = await _db.LifeChapters
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == userId);
        if (chapter == null) return NotFound();
        await LoadProfilePickerAsync();
        return View(chapter);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LifeChapter model, int[]? memberProfileIds)
    {
        var userId = _userManager.GetUserId(User)!;
        var chapter = await _db.LifeChapters
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == userId);
        if (chapter == null) return NotFound();

        ModelState.Remove(nameof(model.OwnerUserId));
        ModelState.Remove(nameof(model.Owner));
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A name is required.");
        }
        if (model.StartYear < 1 || model.StartYear > 2100)
        {
            ModelState.AddModelError(nameof(model.StartYear), "Start year must be between 1 and 2100.");
        }
        if (model.EndYear.HasValue && model.EndYear < model.StartYear)
        {
            ModelState.AddModelError(nameof(model.EndYear), "End year can't be before start year.");
        }
        if (!ModelState.IsValid)
        {
            model.Id = id;
            model.Members = chapter.Members;
            await LoadProfilePickerAsync();
            return View(model);
        }

        chapter.Name        = model.Name.Trim();
        chapter.Category    = model.Category;
        chapter.StartYear   = model.StartYear;
        chapter.EndYear     = model.EndYear;
        chapter.Color       = string.IsNullOrWhiteSpace(model.Color)       ? null : model.Color.Trim();
        chapter.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        chapter.UpdatedAt   = DateTime.UtcNow;

        // Replace membership set: remove rows not in the new list, add
        // rows that aren't already there. Server-side filter to make
        // sure the picked profiles all belong to this user (defence
        // against a tampered form).
        var newIds = (memberProfileIds ?? Array.Empty<int>()).Distinct().ToHashSet();
        var existingIds = chapter.Members.Select(m => m.PersonProfileId).ToHashSet();
        var toRemove = chapter.Members.Where(m => !newIds.Contains(m.PersonProfileId)).ToList();
        foreach (var r in toRemove) _db.LifeChapterMembers.Remove(r);
        var toAdd = newIds.Except(existingIds).ToArray();
        if (toAdd.Length > 0)
        {
            await AddMembersAsync(chapter.Id, toAdd, userId);
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Era updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var chapter = await _db.LifeChapters
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == userId);
        if (chapter == null) return NotFound();
        _db.LifeChapters.Remove(chapter);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Era \"{chapter.Name}\" removed.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>The bubble-timeline visualization. Serialises every
    /// chapter + its members to JSON; the view's client-side script
    /// lays out the bubbles, tiles avatars inside, and handles the
    /// click-to-expand panel.</summary>
    public async Task<IActionResult> Map()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            return RedirectToAction("Index", "Home");
        }

        var chapters = await _db.LifeChapters
            .Where(c => c.OwnerUserId == userId)
            .Include(c => c.Members).ThenInclude(m => m.PersonProfile)
            .OrderBy(c => c.StartYear)
            .ToListAsync();

        var today = DateTime.UtcNow.Year;
        var data = chapters.Select(c => new
        {
            id          = c.Id,
            name        = c.Name,
            category    = c.Category.ToString(),
            startYear   = c.StartYear,
            endYear     = c.EndYear,            // null = ongoing
            color       = c.Color,
            description = c.Description,
            members = c.Members
                .Where(m => m.PersonProfile != null)
                .Select(m => new
                {
                    id        = m.PersonProfileId,
                    name      = string.IsNullOrWhiteSpace(m.PersonProfile!.Nickname)
                                    ? m.PersonProfile.DisplayName
                                    : m.PersonProfile.Nickname,
                    fullName  = m.PersonProfile.DisplayName,
                    avatarUrl = m.PersonProfile.AvatarUrl,
                    initials  = Initials(m.PersonProfile.DisplayName)
                })
                .ToList()
        }).ToList();

        ViewBag.ChaptersJson = System.Text.Json.JsonSerializer.Serialize(data);
        ViewBag.TodayYear    = today;
        ViewBag.ChapterCount = chapters.Count;
        return View();
    }

    private static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Length >= 2 ? parts[0][..2].ToUpper() : parts[0][..1].ToUpper();
    }

    private async Task LoadProfilePickerAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        ViewBag.ProfileChoices = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId)
            .OrderBy(p => p.DisplayName)
            .Select(p => new
            {
                p.Id,
                p.DisplayName,
                p.AvatarUrl,
                p.Kind
            })
            .ToListAsync();
    }

    private async Task AddMembersAsync(int chapterId, int[] profileIds, string ownerUserId)
    {
        // Server-side filter: only profiles the caller actually owns.
        var ownProfileIds = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == ownerUserId && profileIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
        foreach (var pid in ownProfileIds)
        {
            _db.LifeChapterMembers.Add(new LifeChapterMember
            {
                LifeChapterId   = chapterId,
                PersonProfileId = pid,
                CreatedAt       = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
    }
}
