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
        // Channel posts are excluded: they're editorial content owned by the
        // channel, not part of this user's personal story.
        var visiblePosts = _db.LifeEventPosts
            .Where(p => p.OwnerUserId == id && !p.IsDraft && p.ChannelId == null);

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

        // One round-trip for count + oldest + newest event year. The previous
        // three separate queries each ran a full ORDER BY/aggregate.
        var postStats = await visiblePosts
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Oldest = g.Min(p => (int?)p.EventYear),
                Newest = g.Max(p => (int?)p.EventYear)
            })
            .FirstOrDefaultAsync();
        ViewBag.PostCount = postStats?.Count ?? 0;
        ViewBag.OldestEventYear = postStats?.Oldest;
        ViewBag.NewestEventYear = postStats?.Newest;

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

    /// <summary>Records that the current user has dismissed the active site
    /// banner version. Wired to the small × on the banner.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissBanner(int version, string? returnUrl, [FromServices] ISiteSettings siteSettings)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            var current = await siteSettings.GetIntAsync(ISiteSettings.BannerVersion, 0);
            // Trust the smaller of (posted version, current) so a stale form
            // can't accidentally suppress a newer banner.
            user.LastDismissedBannerVersion = Math.Max(user.LastDismissedBannerVersion, Math.Min(version, current));
            await _userManager.UpdateAsync(user);
        }
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Records that the current user has seen the active "what's new"
    /// version. Wired to the modal's close button on Home.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissWhatsNew(int version, [FromServices] ISiteSettings siteSettings)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            var current = await siteSettings.GetIntAsync(ISiteSettings.WhatsNewVersion, 0);
            user.LastSeenWhatsNewVersion = Math.Max(user.LastSeenWhatsNewVersion, Math.Min(version, current));
            await _userManager.UpdateAsync(user);
        }
        return Json(new { ok = true });
    }

    /// <summary>One-click toggle for the per-user "hide channels / hide
    /// biographical" feed filters — wired to the small pill buttons on the
    /// home feed sort bar so the user doesn't have to dive into Settings.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFeedFilter(string filter, string? returnUrl)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        switch (filter)
        {
            case "channels": user.HideChannelsInFeed = !user.HideChannelsInFeed; break;
            case "biographical": user.HideBiographicalInFeed = !user.HideBiographicalInFeed; break;
            default: return BadRequest("Unknown filter");
        }
        await _userManager.UpdateAsync(user);
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    // GET: /Profile/Edit
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        // Per-channel + per-bio mute lists. Stored as CSVs; the Settings
        // page renders a checkbox per item — checked = kept, unchecked = muted.
        var mutedChannels = ParseIdSet(user.MutedChannelIds);
        var mutedBios = new HashSet<string>(
            (user.MutedBiographicalUserIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.Ordinal);

        var allChannels = await _db.Channels
            .OrderBy(c => c.Name)
            .Select(c => new SubscribableItemViewModel
            {
                Id = c.Id.ToString(),
                Name = c.Name,
                Subtitle = c.Description,
                Icon = c.IconEmoji
            })
            .ToListAsync();
        foreach (var item in allChannels)
        {
            item.Kept = !mutedChannels.Contains(int.Parse(item.Id));
        }

        var allBios = await _db.Users
            .Where(u => u.IsBiographical)
            .OrderBy(u => u.DisplayName ?? u.UserName)
            .Select(u => new SubscribableItemViewModel
            {
                Id = u.Id,
                Name = u.DisplayName ?? u.UserName!,
                Subtitle = u.BiographicalEra
            })
            .ToListAsync();
        foreach (var item in allBios)
        {
            item.Kept = !mutedBios.Contains(item.Id);
        }
        ViewBag.AllChannels = allChannels;
        ViewBag.AllBios = allBios;

        var model = new ProfileEditViewModel
        {
            UserName = user.UserName!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Nickname = user.Nickname,
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
            IsCompletelyPrivate = user.IsCompletelyPrivate,
            HideChannelsInFeed = user.HideChannelsInFeed,
            HideBiographicalInFeed = user.HideBiographicalInFeed,
            KeptChannelIds = allChannels.Where(c => c.Kept).Select(c => int.Parse(c.Id)).ToList(),
            KeptBiographicalUserIds = allBios.Where(b => b.Kept).Select(b => b.Id).ToList()
        };

        return View(model);
    }

    private static HashSet<int> ParseIdSet(string? csv)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var n)) set.Add(n);
        }
        return set;
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
        user.Nickname = string.IsNullOrWhiteSpace(model.Nickname) ? null : model.Nickname.Trim();
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

        // Mirror the chosen UI language into the localization cookie so the
        // very next render is in their language — without this, saving the
        // profile updates the column but nothing changes visually until next
        // sign-in (the cookie still says English).
        {
            var allowed = new[] { "en", "it" };
            var picked = (user.PreferredUiLanguage ?? "en").ToLowerInvariant();
            if (!allowed.Contains(picked)) picked = "en";
            Response.Cookies.Append(
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
                    new Microsoft.AspNetCore.Localization.RequestCulture(picked)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = false }
            );
        }

        user.IsCompletelyPrivate = model.IsCompletelyPrivate;
        user.HideChannelsInFeed = model.HideChannelsInFeed;
        user.HideBiographicalInFeed = model.HideBiographicalInFeed;

        // Per-item mute lists. Form posts back the IDs the user wants to
        // KEEP; we invert that against what's currently available to derive
        // the muted list. Items not currently in the available pool (e.g.,
        // a channel that was deleted) are simply forgotten.
        var availableChannelIds = (await _db.Channels.Select(c => c.Id).ToListAsync()).ToHashSet();
        var keptChannelIds = (model.KeptChannelIds ?? new List<int>()).ToHashSet();
        var mutedChannelIds = availableChannelIds.Where(id => !keptChannelIds.Contains(id)).ToList();
        user.MutedChannelIds = mutedChannelIds.Count == 0
            ? null
            : string.Join(",", mutedChannelIds);

        var availableBioIds = await _db.Users.Where(u => u.IsBiographical).Select(u => u.Id).ToListAsync();
        var keptBioIds = new HashSet<string>(model.KeptBiographicalUserIds ?? new List<string>(), StringComparer.Ordinal);
        var mutedBioIds = availableBioIds.Where(id => !keptBioIds.Contains(id)).ToList();
        user.MutedBiographicalUserIds = mutedBioIds.Count == 0
            ? null
            : string.Join(",", mutedBioIds);

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
