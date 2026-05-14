using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// Hub page that gathers the four "people in your life" tools under
/// one entry in the nav: People Profiles, Friendship graph, Life
/// chapters, Life map. Same premium gate as the underlying features
/// so we don't surface a hub that leads only to a wall.
/// </summary>
[Authorize]
public class ConnectionsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPremiumService _premium;

    public ConnectionsController(
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
            TempData["Info"] = "Connections aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        // Counts on the tiles so the hub feels alive rather than four
        // identical buttons. Best-effort — if any of these throws we
        // just render zeros.
        int profileCount = 0, friendKindCount = 0, milestoneCount = 0, chapterCount = 0;
        try
        {
            profileCount = await _db.PersonProfiles
                .CountAsync(p => p.CreatorUserId == userId);
            friendKindCount = await _db.PersonProfiles
                .CountAsync(p => p.CreatorUserId == userId && p.Kind != PersonProfileKind.Family);
            milestoneCount = await _db.ProfileMilestones
                .CountAsync(m => m.PersonProfile != null && m.PersonProfile.CreatorUserId == userId);
            chapterCount = await _db.LifeChapters
                .CountAsync(c => c.OwnerUserId == userId);
        }
        catch { /* hub renders with zeros if a table is mid-migration */ }

        ViewBag.ProfileCount    = profileCount;
        ViewBag.FriendKindCount = friendKindCount;
        ViewBag.MilestoneCount  = milestoneCount;
        ViewBag.ChapterCount    = chapterCount;
        return View();
    }
}
