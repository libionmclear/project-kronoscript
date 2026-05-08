using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly IPermissionService _permissionService;
    private readonly IFileStorageService _files;
    private readonly ApplicationDbContext _db;

    public ProfileController(UserManager<ApplicationUser> userManager, IWebHostEnvironment env, IPermissionService permissionService, IFileStorageService files, ApplicationDbContext db)
    {
        _userManager = userManager;
        _env = env;
        _permissionService = permissionService;
        _files = files;
        _db = db;
    }

    private static bool CanSeeField(ProfileFieldVisibility v, FriendTier? viewerTier, bool isOwner)
    {
        if (isOwner) return true;
        return v switch
        {
            ProfileFieldVisibility.Public  => true,
            ProfileFieldVisibility.Friends => viewerTier == FriendTier.Friend || viewerTier == FriendTier.Family,
            ProfileFieldVisibility.Family  => viewerTier == FriendTier.Family,
            ProfileFieldVisibility.Private => false,
            _ => false
        };
    }

    // GET: /Profile/{id?} (view someone's profile; if no id, redirect to own)
    [HttpGet]
    public async Task<IActionResult> Index(string? id)
    {
        if (string.IsNullOrEmpty(id))
            id = _userManager.GetUserId(User)!;

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User)!;
        var isOwner = currentUserId == id;
        ViewBag.IsOwner = isOwner;

        var tier = isOwner ? (FriendTier?)null : await _permissionService.GetViewerTierAsync(currentUserId, id);

        ViewBag.ShowBirthDate       = CanSeeField(user.BirthDateVisibility,       tier, isOwner);
        ViewBag.ShowGender          = CanSeeField(user.GenderVisibility,          tier, isOwner);
        ViewBag.ShowBirthPlace      = CanSeeField(user.BirthPlaceVisibility,      tier, isOwner);
        ViewBag.ShowCurrentLocation = CanSeeField(user.CurrentLocationVisibility, tier, isOwner);
        ViewBag.ShowNationalities   = CanSeeField(user.NationalitiesVisibility,   tier, isOwner);

        // Stats strip + recent stories — same visibility filter as the timeline.
        var visiblePosts = _db.LifeEventPosts
            .Where(p => p.OwnerUserId == id && !p.IsDraft);

        if (!isOwner)
        {
            // Match the post's audience to the viewer's tier. Family > Friend > Acquaintance > none.
            var maxAudience = tier switch
            {
                FriendTier.Family       => PostVisibility.Family,
                FriendTier.Friend       => PostVisibility.Friends,
                FriendTier.Acquaintance => PostVisibility.Acquaintances,
                _                       => PostVisibility.Public
            };
            visiblePosts = visiblePosts.Where(p =>
                p.Visibility == PostVisibility.Public ||
                (maxAudience == PostVisibility.Acquaintances && p.Visibility == PostVisibility.Acquaintances) ||
                (maxAudience == PostVisibility.Friends && (p.Visibility == PostVisibility.Acquaintances || p.Visibility == PostVisibility.Friends)) ||
                (maxAudience == PostVisibility.Family && (p.Visibility == PostVisibility.Acquaintances || p.Visibility == PostVisibility.Friends || p.Visibility == PostVisibility.Family))
            );
        }

        ViewBag.PostCount = await visiblePosts.CountAsync();
        ViewBag.OldestEventYear = await visiblePosts.OrderBy(p => p.EventYear).Select(p => (int?)p.EventYear).FirstOrDefaultAsync();
        ViewBag.NewestEventYear = await visiblePosts.OrderByDescending(p => p.EventYear).Select(p => (int?)p.EventYear).FirstOrDefaultAsync();

        ViewBag.RecentStories = await visiblePosts
            .OrderByDescending(p => p.CreatedAt)
            .Include(p => p.Media)
            .Take(6)
            .ToListAsync();

        ViewBag.ConnectionsCount = await _db.FriendConnections
            .CountAsync(f => (f.RequesterUserId == id || f.AddresseeUserId == id)
                          && f.Status == FriendConnectionStatus.Accepted);

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFieldVisibility(string field, ProfileFieldVisibility visibility)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        switch (field)
        {
            case "BirthDate":       user.BirthDateVisibility       = visibility; break;
            case "Gender":          user.GenderVisibility          = visibility; break;
            case "BirthPlace":      user.BirthPlaceVisibility      = visibility; break;
            case "CurrentLocation": user.CurrentLocationVisibility = visibility; break;
            case "Nationalities":   user.NationalitiesVisibility   = visibility; break;
            default: return BadRequest("Unknown field");
        }
        await _userManager.UpdateAsync(user);
        return Json(new { ok = true, visibility = (int)visibility });
    }

    // GET: /Profile/Edit
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        var model = new ProfileEditViewModel
        {
            UserName = user.UserName!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            BirthYear = user.BirthYear,
            BirthMonth = user.BirthMonth,
            BirthDay = user.BirthDay,
            HideBirthYear = user.HideBirthYear,
            Gender = user.Gender,
            BirthPlace = user.BirthPlace,
            CurrentLocation = user.CurrentLocation,
            ExistingPhotoUrl = user.ProfilePhotoUrl,
            ExistingCardBackgroundUrl = user.ProfileCardBackgroundUrl,
            ShowOnlineStatus = user.ShowOnlineStatus,
            Nationalities = user.Nationalities,
            PreferredReadingLanguage = user.PreferredReadingLanguage,
            PreferredUiLanguage = user.PreferredUiLanguage,
            IsCompletelyPrivate = user.IsCompletelyPrivate
        };

        return View(model);
    }

    // POST: /Profile/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProfileEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        user.UserName = model.UserName;
        user.FirstName = model.FirstName;
        user.LastName = model.LastName;
        user.DisplayName = !string.IsNullOrWhiteSpace(model.FirstName) || !string.IsNullOrWhiteSpace(model.LastName)
            ? $"{model.FirstName} {model.LastName}".Trim()
            : model.UserName;
        user.BirthYear = model.BirthYear;
        user.BirthMonth = model.BirthMonth;
        user.BirthDay = model.BirthDay;
        user.HideBirthYear = model.HideBirthYear;
        user.Gender = model.Gender;
        user.BirthPlace = model.BirthPlace;
        user.CurrentLocation = model.CurrentLocation;
        user.ShowOnlineStatus = model.ShowOnlineStatus;
        user.Nationalities = model.Nationalities;
        user.PreferredReadingLanguage = string.IsNullOrWhiteSpace(model.PreferredReadingLanguage) ? null : model.PreferredReadingLanguage.Trim();
        user.PreferredUiLanguage = string.IsNullOrWhiteSpace(model.PreferredUiLanguage) ? null : model.PreferredUiLanguage.Trim();
        user.IsCompletelyPrivate = model.IsCompletelyPrivate;

        // Handle photo upload
        if (model.ProfilePhoto != null && model.ProfilePhoto.Length > 0)
        {
            // GUID prefix so the URL changes on re-upload — defeats the browser
            // cache that otherwise pins the old image to {user.Id}.{ext}.
            var fileName = $"{user.Id}-{Guid.NewGuid():N}{Path.GetExtension(model.ProfilePhoto.FileName)}";
            using var s = model.ProfilePhoto.OpenReadStream();
            user.ProfilePhotoUrl = await _files.UploadAsync(s, "profiles", fileName, model.ProfilePhoto.ContentType);
        }

        // Handle card background upload
        if (model.ProfileCardBackground != null && model.ProfileCardBackground.Length > 0)
        {
            var bgName = $"{user.Id}-{Guid.NewGuid():N}{Path.GetExtension(model.ProfileCardBackground.FileName)}";
            using var s = model.ProfileCardBackground.OpenReadStream();
            user.ProfileCardBackgroundUrl = await _files.UploadAsync(s, "profile-bg", bgName, model.ProfileCardBackground.ContentType);
        }

        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            TempData["Success"] = "Profile updated!";
            return RedirectToAction("Index");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    // GET: /Profile/ChangePassword
    [HttpGet]
    public IActionResult ChangePassword() => View();

    // POST: /Profile/ChangePassword
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            TempData["Success"] = "Password changed successfully!";
            return RedirectToAction("Index");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    // ───── Block / unblock ──────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(string id)
    {
        var meId = _userManager.GetUserId(User)!;
        if (string.IsNullOrEmpty(id) || id == meId) return RedirectToAction(nameof(Index), new { id });

        var exists = await _db.UserBlocks.AnyAsync(b => b.BlockerUserId == meId && b.BlockedUserId == id);
        if (!exists)
        {
            _db.UserBlocks.Add(new UserBlock { BlockerUserId = meId, BlockedUserId = id, CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "User blocked. They won't appear in your search or feed.";
        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unblock(string id)
    {
        var meId = _userManager.GetUserId(User)!;
        var row = await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerUserId == meId && b.BlockedUserId == id);
        if (row != null)
        {
            _db.UserBlocks.Remove(row);
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Unblocked.";
        return RedirectToAction("Edit");
    }
}
