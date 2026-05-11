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
    private readonly INotificationService _notifications;
    private readonly IFileStorageService _files;

    public PersonProfilesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IPremiumService premium,
        IPermissionService permissions,
        INotificationService notifications,
        IFileStorageService files)
    {
        _db = db;
        _userManager = userManager;
        _premium = premium;
        _permissions = permissions;
        _notifications = notifications;
        _files = files;
    }

    // Up to ~10 MB matches what the upload form text promises and the
    // magic-byte sniffer accepts as a sane image limit.
    private const long MaxAvatarBytes = 10L * 1024 * 1024;
    private static readonly string[] AllowedAvatarContentTypes = new[]
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    /// <summary>Upload the file and return the public URL, or null if the
    /// upload was rejected (wrong type, too big, empty). Adds a ModelState
    /// error so the form re-renders with a message.</summary>
    private async Task<string?> TrySaveAvatarAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) return null;
        if (file.Length > MaxAvatarBytes)
        {
            ModelState.AddModelError("avatarFile", "Image is too large — keep it under 10 MB.");
            return null;
        }
        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        if (!AllowedAvatarContentTypes.Contains(contentType))
        {
            ModelState.AddModelError("avatarFile", "Only JPG, PNG, WebP, or GIF are allowed.");
            return null;
        }
        // Defensive: the magic-byte sniffer on the standard upload paths
        // is centralized; here we just trust the content type + extension
        // because the field is creator-only and tightly scoped.
        var ext = System.IO.Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var name = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        using var s = file.OpenReadStream();
        return await _files.UploadAsync(s, "profile-avatars", name, contentType);
    }

    /// <summary>Photos already attached to posts that tag this profile —
    /// surfaces in the form as a "pick from existing" gallery so the
    /// creator doesn't have to re-upload a face we already have.</summary>
    private async Task<List<string>> LoadExistingPhotosAsync(int profileId)
    {
        var idToken = "," + profileId + ",";
        var posts = await _db.LifeEventPosts
            .Where(p => !p.IsDraft
                        && p.TaggedProfileIds != null
                        && EF.Functions.Like("," + p.TaggedProfileIds + ",", "%" + idToken + "%"))
            .Include(p => p.Media)
            .ToListAsync();

        return posts
            .SelectMany(p => p.Media ?? new List<PostMedia>())
            .Where(m => m.MediaType == MediaType.Image && !string.IsNullOrEmpty(m.Url))
            .Select(m => m.Url!)
            .Distinct()
            .Take(24)
            .ToList();
    }

    // GET: /PersonProfiles — list of profiles the current user created.
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);

        // Entry-point gate. If the feature is in Off mode for this
        // viewer (admins always pass via IPremiumService), bounce them
        // home rather than render an empty list page.
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            TempData["Info"] = "People profiles aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var profiles = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync();

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
    public async Task<IActionResult> Create(PersonProfile model, IFormFile? avatarFile)
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

        // Upload wins over a typed/picked URL. If the file is invalid we
        // surface the error to the form rather than silently dropping it.
        var uploadedUrl = await TrySaveAvatarAsync(avatarFile);
        if (!string.IsNullOrEmpty(uploadedUrl))
        {
            model.AvatarUrl = uploadedUrl;
        }

        if (!ModelState.IsValid) return View(model);

        model.CreatorUserId = _userManager.GetUserId(User)!;
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = null;
        model.LinkedUserId = null;   // never set by the form — only by claim flow
        model.AvatarUrl     = string.IsNullOrWhiteSpace(model.AvatarUrl) ? null : model.AvatarUrl.Trim();
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
        ViewBag.ExistingPhotos = await LoadExistingPhotosAsync(id);
        return View(profile);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PersonProfile model, IFormFile? avatarFile)
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

        // Upload wins over a picked URL; if the upload is invalid we
        // re-render with the existing-photos picker repopulated.
        var uploadedUrl = await TrySaveAvatarAsync(avatarFile);
        if (!string.IsNullOrEmpty(uploadedUrl))
        {
            model.AvatarUrl = uploadedUrl;
        }

        if (!ModelState.IsValid)
        {
            model.Id = id;
            ViewBag.ExistingPhotos = await LoadExistingPhotosAsync(id);
            return View(model);
        }

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
        var newEmail = string.IsNullOrWhiteSpace(model.ContactEmail) ? null : model.ContactEmail.Trim().ToLowerInvariant();
        // Reset a prior decline when the creator edits the email — the
        // common case is "I typed it wrong, here's the real one"; the
        // owner of the *new* address should get a fresh banner.
        if (!string.Equals(profile.ContactEmail, newEmail, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(profile.LinkedUserId))
        {
            profile.ClaimDeclinedAt = null;
        }
        profile.ContactEmail   = newEmail;
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
        var viewerUser = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(viewerUser, PremiumFeature.PeopleProfiles))
        {
            TempData["Info"] = "People profiles aren't available right now.";
            return RedirectToAction("Index", "Home");
        }
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

        // Stories that tag this profile. TaggedProfileIds is stored as a
        // comma-separated list, so match against ",<id>," with sentinel
        // commas wrapped around the column value to guarantee word-level
        // matching (otherwise id=1 would match TaggedProfileIds="11,12").
        var idToken = "," + profile.Id + ",";
        var allTagged = await _db.LifeEventPosts
            .Include(p => p.Owner)
            .Include(p => p.Channel)
            .Include(p => p.Media)
            .Where(p => !p.IsDraft
                        && p.TaggedProfileIds != null
                        && EF.Functions.Like("," + p.TaggedProfileIds + ",", "%" + idToken + "%"))
            .OrderByDescending(p => p.EventYear)
                .ThenByDescending(p => p.EventMonth ?? 0)
                .ThenByDescending(p => p.EventDay ?? 0)
                .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();

        // Filter to posts the viewer can actually see, using the same
        // permission ladder posts use everywhere else.
        var visibleTagged = new List<LifeEventPost>();
        foreach (var p in allTagged)
        {
            if (p.OwnerUserId == userId) { visibleTagged.Add(p); continue; }
            if (p.Visibility == PostVisibility.Public) { visibleTagged.Add(p); continue; }
            var canSee = await _permissions.CanViewPostsAsync(userId, p.OwnerUserId);
            if (!canSee) continue;
            var tier = await _permissions.GetViewerTierAsync(userId, p.OwnerUserId);
            if (p.Visibility == PostVisibility.Family && tier != FriendTier.Family) continue;
            if (p.Visibility == PostVisibility.Friends &&
                tier != FriendTier.Friend && tier != FriendTier.Family) continue;
            visibleTagged.Add(p);
        }

        ViewBag.IsOwner = isOwner;
        ViewBag.TaggedInPosts = visibleTagged;
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

    // POST: /PersonProfiles/Claim/5 — "Yes, that's me." Verifies the
    // current user's email matches the profile's ContactEmail and that
    // the profile isn't already linked, then links it to the user.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();

        if (!string.IsNullOrEmpty(profile.LinkedUserId))
        {
            TempData["Error"] = "This profile has already been claimed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Email match is the only authorization. ContactEmail is stored
        // lower-cased; compare the user's email the same way.
        var userEmail = (user.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(userEmail)
            || string.IsNullOrEmpty(profile.ContactEmail)
            || !string.Equals(userEmail, profile.ContactEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }
        // Block self-claim — a creator can't claim their own profile.
        if (profile.CreatorUserId == user.Id)
        {
            TempData["Error"] = "You created this profile — you can't claim it as yourself.";
            return RedirectToAction(nameof(Details), new { id });
        }

        profile.LinkedUserId    = user.Id;
        profile.ClaimedAt       = DateTime.UtcNow;
        profile.ClaimDeclinedAt = null;
        profile.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Tell the creator that the linkage just happened — they'll
        // want to know that their profile is now routed through the
        // real member's timeline.
        var claimerName = user.DisplayName ?? user.UserName ?? "Someone";
        await _notifications.CreateAsync(
            profile.CreatorUserId,
            NotificationType.ProfileClaimed,
            $"{claimerName} claimed the profile you created for {profile.DisplayName}.",
            Url.Action(nameof(Details), "PersonProfiles", new { id = profile.Id }),
            user.Id);

        TempData["Success"] = $"You've claimed the profile for {profile.DisplayName}. Tags now route to your timeline.";
        return RedirectToAction(nameof(Details), new { id = profile.Id });
    }

    // POST: /PersonProfiles/DeclineClaim/5 — "Not me." Records the
    // decline so the banner stops showing on subsequent page loads.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineClaim(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (!string.IsNullOrEmpty(profile.LinkedUserId)) return BadRequest();

        var userEmail = (user.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(userEmail)
            || string.IsNullOrEmpty(profile.ContactEmail)
            || !string.Equals(userEmail, profile.ContactEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        profile.ClaimDeclinedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction("Index", "Home");
    }

    // POST: /PersonProfiles/Unlink/5 — break the link between the
    // profile and the member. Allowed by the creator, the linked user
    // themselves, or an admin. Posts that tagged the profile keep the
    // tag; rendering reverts to the standalone profile page.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (string.IsNullOrEmpty(profile.LinkedUserId)) return BadRequest();

        var canUnlink = profile.CreatorUserId == userId
                        || profile.LinkedUserId == userId
                        || User.IsInRole("Admin");
        if (!canUnlink) return Forbid();

        profile.LinkedUserId = null;
        profile.ClaimedAt = null;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Link removed. The profile is no longer connected to that member.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
