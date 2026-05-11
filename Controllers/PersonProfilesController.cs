using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// CRUD for People Profiles — premium-tagged "memory cards" for people
/// the user writes about who aren't on the site (deceased family,
/// distant relatives, etc.). Creating is gated through IPremiumService
/// (today: free for everyone because enforcement is off). Listing and
/// viewing are always allowed so existing profiles never disappear if
/// a subscription lapses.
/// </summary>
[Authorize]
public class PersonProfilesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPremiumService _premium;
    private readonly IPermissionService _permissions;

    public PersonProfilesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IPremiumService premium,
        IPermissionService permissions)
    {
        _db = db;
        _userManager = userManager;
        _premium = premium;
        _permissions = permissions;
    }

    // GET: /PersonProfiles — list of profiles the current user created.
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var profiles = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync();

        var user = await _userManager.GetUserAsync(User);
        ViewBag.CanCreate = await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles);
        return View(profiles);
    }

    // GET: /PersonProfiles/Create
    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            TempData["Error"] = "Creating people profiles requires a premium subscription.";
            return RedirectToAction(nameof(Index));
        }
        return View(new PersonProfile());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PersonProfile model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(model.DisplayName))
        {
            ModelState.AddModelError(nameof(model.DisplayName), "A name is required.");
        }
        if (model.BirthYear.HasValue && model.DeathYear.HasValue && model.DeathYear < model.BirthYear)
        {
            ModelState.AddModelError(nameof(model.DeathYear), "Death year can't be earlier than birth year.");
        }
        if (!ModelState.IsValid) return View(model);

        model.CreatorUserId = _userManager.GetUserId(User)!;
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = null;
        model.LinkedUserId = null;   // never set by the form — only by claim flow
        // Normalize email — lower-cased, trimmed — so passive match
        // works case-insensitively against the AspNetUsers email.
        model.ContactEmail = string.IsNullOrWhiteSpace(model.ContactEmail)
            ? null
            : model.ContactEmail.Trim().ToLowerInvariant();

        _db.PersonProfiles.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Profile for {model.DisplayName} created.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // GET: /PersonProfiles/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (profile.CreatorUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles)
            && !User.IsInRole("Admin"))
        {
            // Lapsed subscription: viewing/listing stays open but
            // editing is gated. Send them back to Details with a flag.
            TempData["Error"] = "Editing people profiles requires a premium subscription.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(profile);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PersonProfile model)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (profile.CreatorUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles)
            && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(model.DisplayName))
        {
            ModelState.AddModelError(nameof(model.DisplayName), "A name is required.");
        }
        if (model.BirthYear.HasValue && model.DeathYear.HasValue && model.DeathYear < model.BirthYear)
        {
            ModelState.AddModelError(nameof(model.DeathYear), "Death year can't be earlier than birth year.");
        }
        if (!ModelState.IsValid) { model.Id = id; return View(model); }

        profile.DisplayName    = model.DisplayName.Trim();
        profile.Relation       = string.IsNullOrWhiteSpace(model.Relation) ? null : model.Relation.Trim();
        profile.AvatarUrl      = string.IsNullOrWhiteSpace(model.AvatarUrl) ? null : model.AvatarUrl.Trim();
        profile.BirthYear      = model.BirthYear;
        profile.BirthPlace     = string.IsNullOrWhiteSpace(model.BirthPlace) ? null : model.BirthPlace.Trim();
        profile.DeathYear      = model.DeathYear;
        profile.DeathPlace     = string.IsNullOrWhiteSpace(model.DeathPlace) ? null : model.DeathPlace.Trim();
        profile.DatesEstimated = model.DatesEstimated;
        profile.Bio            = string.IsNullOrWhiteSpace(model.Bio)     ? null : model.Bio.Trim();
        profile.Notes          = string.IsNullOrWhiteSpace(model.Notes)   ? null : model.Notes.Trim();
        profile.Sources        = string.IsNullOrWhiteSpace(model.Sources) ? null : model.Sources.Trim();
        profile.Visibility     = model.Visibility;
        profile.ContactEmail   = string.IsNullOrWhiteSpace(model.ContactEmail) ? null : model.ContactEmail.Trim().ToLowerInvariant();
        profile.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Details), new { id = profile.Id });
    }

    // GET: /PersonProfiles/Details/5 — respects the profile's
    // visibility relative to the viewer.
    public async Task<IActionResult> Details(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles
            .Include(p => p.Creator)
            .Include(p => p.LinkedUser)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();

        var isOwner = profile.CreatorUserId == userId;
        if (!isOwner && profile.Visibility != PostVisibility.Public)
        {
            var canSee = await _permissions.CanViewPostsAsync(userId, profile.CreatorUserId);
            if (!canSee) return Forbid();
            // Family-only / Friends-only further restrict — match the
            // same tier ladder posts use.
            var tier = await _permissions.GetViewerTierAsync(userId, profile.CreatorUserId);
            if (profile.Visibility == PostVisibility.Family && tier != FriendTier.Family) return Forbid();
            if (profile.Visibility == PostVisibility.Friends &&
                tier != FriendTier.Friend && tier != FriendTier.Family) return Forbid();
        }

        ViewBag.IsOwner = isOwner;
        return View(profile);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (profile.CreatorUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        _db.PersonProfiles.Remove(profile);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Profile for {profile.DisplayName} removed.";
        return RedirectToAction(nameof(Index));
    }
}
