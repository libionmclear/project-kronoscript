using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Controllers;

[Authorize]
public class WorkingIndexController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public WorkingIndexController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var existing = await _db.WorkingIndexEntries
            .Where(e => e.OwnerUserId == userId)
            .ToListAsync();

        var user = await _userManager.FindByIdAsync(userId);
        var nowYear = DateTime.UtcNow.Year;
        int from;
        if (user?.BirthYear.HasValue == true && user.BirthYear.Value > 0)
            from = user.BirthYear.Value;
        else if (existing.Any())
            from = existing.Min(e => e.Year);
        else
            from = nowYear - 30;

        // Cap to 130 years to avoid runaway scaffolding
        if (nowYear - from > 130) from = nowYear - 130;

        var byYear = existing.ToDictionary(e => e.Year);
        var rows = new List<MyStoryTold.Models.WorkingIndexEntry>();
        for (int y = nowYear; y >= from; y--)
        {
            rows.Add(byYear.TryGetValue(y, out var e)
                ? e
                : new MyStoryTold.Models.WorkingIndexEntry { Id = 0, OwnerUserId = userId, Year = y });
        }
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upsert(int year, string field, string? value)
    {
        var userId = _userManager.GetUserId(User)!;
        if (year < -3000 || year > 2100) return BadRequest("Invalid year");

        var entry = await _db.WorkingIndexEntries.FirstOrDefaultAsync(e => e.OwnerUserId == userId && e.Year == year);
        if (entry == null)
        {
            entry = new WorkingIndexEntry
            {
                OwnerUserId = userId,
                Year = year,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.WorkingIndexEntries.Add(entry);
        }
        switch (field)
        {
            case "MainEvent":    entry.MainEvent    = value; break;
            case "Residence":    entry.Residence    = value; break;
            case "SchoolJob":    entry.SchoolJob    = value; break;
            case "Relationship": entry.Relationship = value; break;
            case "Family":       entry.Family       = value; break;
            case "Vacation":     entry.Vacation     = value; break;
            case "Other":        entry.Other        = value; break;
            case "Notes":        entry.Notes        = value; break;
            case "Mood":
                if (Enum.TryParse<WorkingIndexMood>(value, out var m)) entry.Mood = m;
                break;
            default: return BadRequest("Unknown field");
        }
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Json(new { ok = true, id = entry.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int year)
    {
        var userId = _userManager.GetUserId(User)!;
        if (year < -3000 || year > 2100) return BadRequest("Invalid year");

        var existing = await _db.WorkingIndexEntries
            .FirstOrDefaultAsync(e => e.OwnerUserId == userId && e.Year == year);
        if (existing != null)
        {
            return Json(new { id = existing.Id, year = existing.Year, duplicate = true });
        }

        var entry = new WorkingIndexEntry
        {
            OwnerUserId = userId,
            Year = year,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.WorkingIndexEntries.Add(entry);
        await _db.SaveChangesAsync();
        return Json(new { id = entry.Id, year = entry.Year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, string field, string? value)
    {
        var userId = _userManager.GetUserId(User)!;
        var entry = await _db.WorkingIndexEntries.FirstOrDefaultAsync(e => e.Id == id && e.OwnerUserId == userId);
        if (entry == null) return NotFound();

        switch (field)
        {
            case "MainEvent":    entry.MainEvent    = value; break;
            case "Residence":    entry.Residence    = value; break;
            case "SchoolJob":    entry.SchoolJob    = value; break;
            case "Relationship": entry.Relationship = value; break;
            case "Family":       entry.Family       = value; break;
            case "Vacation":     entry.Vacation     = value; break;
            case "Other":        entry.Other        = value; break;
            case "Notes":        entry.Notes        = value; break;
            case "Mood":
                if (Enum.TryParse<WorkingIndexMood>(value, out var m)) entry.Mood = m;
                break;
            default: return BadRequest("Unknown field");
        }
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var entry = await _db.WorkingIndexEntries.FirstOrDefaultAsync(e => e.Id == id && e.OwnerUserId == userId);
        if (entry == null) return NotFound();
        _db.WorkingIndexEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }
}
