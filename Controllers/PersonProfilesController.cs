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
    private readonly IFriendService _friends;

    public PersonProfilesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IPremiumService premium,
        IPermissionService permissions,
        INotificationService notifications,
        IFileStorageService files,
        IFriendService friends)
    {
        _db = db;
        _userManager = userManager;
        _premium = premium;
        _permissions = permissions;
        _notifications = notifications;
        _files = files;
        _friends = friends;
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

        // The form doesn't post CreatorUserId — we set it ourselves below.
        // ASP.NET's nullable-reference-type validation treats non-nullable
        // string properties as Required, which makes ModelState.IsValid
        // false on every submit and silently re-renders the empty form
        // with no visible error. Strip those entries before validating.
        ModelState.Remove(nameof(model.CreatorUserId));
        ModelState.Remove(nameof(model.Creator));
        ModelState.Remove(nameof(model.LinkedUserId));
        ModelState.Remove(nameof(model.LinkedUser));

        if (string.IsNullOrWhiteSpace(model.DisplayName))
        {
            ModelState.AddModelError(nameof(model.DisplayName), "A name is required.");
        }
        // Strict biological sex — required, Male or Female only. The
        // family-tree layout uses it to place husbands on the left and
        // wives on the right inside each couple, and the kinship
        // calculator uses it to pick Father vs Mother / Niece vs Nephew
        // / etc. Anything else breaks both, so we reject the submit.
        var sex = (model.Gender ?? "").Trim();
        if (sex != "Male" && sex != "Female")
        {
            ModelState.AddModelError(nameof(model.Gender), "Pick Male or Female.");
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
        model.Nickname      = string.IsNullOrWhiteSpace(model.Nickname)  ? null : model.Nickname.Trim();
        model.Gender        = string.IsNullOrWhiteSpace(model.Gender)    ? null : model.Gender.Trim();
        // Normalize email — lower-cased, trimmed — so passive match
        // works case-insensitively against the AspNetUsers email.
        model.ContactEmail = string.IsNullOrWhiteSpace(model.ContactEmail)
            ? null
            : model.ContactEmail.Trim().ToLowerInvariant();

        try
        {
            _db.PersonProfiles.Add(model);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Surface the actual reason instead of a silent redirect — the
            // user (or admin reading the screenshot) can act on it. Common
            // causes: a schema column missing on an older deploy, an
            // unexpectedly-long string field, or a unique-index collision.
            ModelState.AddModelError(string.Empty,
                "Could not save the profile: " + (ex.InnerException?.Message ?? ex.Message));
            return View(model);
        }

        TempData["Success"] = $"Profile for {model.DisplayName} created.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    /// <summary>True if the viewer is allowed to edit the given profile —
    /// creator, app admin, OR an admin/co-admin of the FamilyGroup that
    /// owns the profile (group-owned profiles are collaboratively edited).</summary>
    private async Task<bool> CanEditProfileAsync(PersonProfile profile, string viewerUserId)
    {
        if (profile.CreatorUserId == viewerUserId) return true;
        if (User.IsInRole("Admin")) return true;
        if (profile.FamilyGroupId.HasValue)
        {
            var role = await _db.FamilyGroupMembers
                .Where(m => m.FamilyGroupId == profile.FamilyGroupId.Value && m.UserId == viewerUserId)
                .Select(m => (FamilyGroupRole?)m.Role)
                .FirstOrDefaultAsync();
            if (role == FamilyGroupRole.Admin || role == FamilyGroupRole.CoAdmin) return true;
        }
        return false;
    }

    // GET: /PersonProfiles/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (!await CanEditProfileAsync(profile, userId)) return Forbid();

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
        if (!await CanEditProfileAsync(profile, userId))
        {
            return Forbid();
        }
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles)
            && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        // Same ModelState-cleanup as Create — the form doesn't post the
        // system-set fields, but ASP.NET's NRT validation flags them.
        ModelState.Remove(nameof(model.CreatorUserId));
        ModelState.Remove(nameof(model.Creator));
        ModelState.Remove(nameof(model.LinkedUserId));
        ModelState.Remove(nameof(model.LinkedUser));

        if (string.IsNullOrWhiteSpace(model.DisplayName))
        {
            ModelState.AddModelError(nameof(model.DisplayName), "A name is required.");
        }
        // Strict biological sex — required, Male or Female only. The
        // family-tree layout uses it to place husbands on the left and
        // wives on the right inside each couple, and the kinship
        // calculator uses it to pick Father vs Mother / Niece vs Nephew
        // / etc. Anything else breaks both, so we reject the submit.
        var sex = (model.Gender ?? "").Trim();
        if (sex != "Male" && sex != "Female")
        {
            ModelState.AddModelError(nameof(model.Gender), "Pick Male or Female.");
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
        profile.Nickname       = string.IsNullOrWhiteSpace(model.Nickname) ? null : model.Nickname.Trim();
        profile.Gender         = string.IsNullOrWhiteSpace(model.Gender)   ? null : model.Gender.Trim();
        profile.Kind           = model.Kind;
        profile.Relation       = string.IsNullOrWhiteSpace(model.Relation) ? null : model.Relation.Trim();
        profile.AvatarUrl      = string.IsNullOrWhiteSpace(model.AvatarUrl) ? null : model.AvatarUrl.Trim();
        profile.BirthYear      = model.BirthYear;
        profile.BirthPlace     = string.IsNullOrWhiteSpace(model.BirthPlace) ? null : model.BirthPlace.Trim();
        profile.DeathYear      = model.DeathYear;
        profile.DeathPlace     = string.IsNullOrWhiteSpace(model.DeathPlace) ? null : model.DeathPlace.Trim();
        profile.MetYear        = model.MetYear;
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

        // Photos where this profile is face-tagged. Wrapped in try/catch so
        // a hiccup in the photo-tag pipeline (e.g. MediaPersonTags table
        // not yet migrated on an older snapshot) never breaks Details —
        // the user can still see the profile + edit it.
        var visiblePhotoPostGroups = new List<MediaPersonTagGroup>();
        try
        {
            var photoTagRows = await _db.MediaPersonTags
                .Where(t => t.TargetProfileId == profile.Id)
                .Include(t => t.Media).ThenInclude(m => m!.Post).ThenInclude(p => p!.Owner)
                .Include(t => t.Media).ThenInclude(m => m!.Post).ThenInclude(p => p!.Channel)
                .ToListAsync();

            var seenPostIds = new HashSet<int>();
            foreach (var t in photoTagRows.OrderByDescending(t => t.CreatedAt))
            {
                var post = t.Media?.Post;
                if (post == null || post.IsDraft) continue;
                if (seenPostIds.Contains(post.Id)) continue;
                var include = false;
                if (post.OwnerUserId == userId) include = true;
                else if (post.Visibility == PostVisibility.Public) include = true;
                else
                {
                    var canSee = await _permissions.CanViewPostsAsync(userId, post.OwnerUserId);
                    if (canSee)
                    {
                        var tier = await _permissions.GetViewerTierAsync(userId, post.OwnerUserId);
                        include = post.Visibility switch
                        {
                            PostVisibility.Family   => tier == FriendTier.Family,
                            PostVisibility.Friends  => tier == FriendTier.Friend || tier == FriendTier.Family,
                            _ => true
                        };
                    }
                }
                if (!include) continue;
                var allTagsForPost = photoTagRows
                    .Where(x => x.Media?.PostId == post.Id)
                    .ToList();
                visiblePhotoPostGroups.Add(new MediaPersonTagGroup { Post = post, Tags = allTagsForPost });
                seenPostIds.Add(post.Id);
            }
        }
        catch
        {
            // Swallow — the page renders without the photo-tag section.
        }

        // When the creator is viewing their own un-claimed profile,
        // surface the list of their friends/family-tier connections so
        // they can directly link the NPC card to a real member they've
        // already vouched for. This is Tier 1 of the claim authority
        // hierarchy — creator authority is enough; no joiner approval
        // round-trip required.
        if (isOwner && string.IsNullOrEmpty(profile.LinkedUserId))
        {
            var friendList = await _friends.GetFriendListAsync(userId);
            var linkable = friendList.Friends
                .Select(f => f.User)
                .Where(u => u.Id != userId)
                .OrderBy(u => u.DisplayName ?? u.UserName)
                .ToList();
            ViewBag.LinkableMembers = linkable;
        }

        // Pending claims on this profile (Tier 2/3). The creator sees
        // them and clicks Approve/Deny; the claimant sees their own
        // pending claim with a Withdraw option.
        var pendingClaims = await _db.ProfileClaims
            .Where(c => c.PersonProfileId == id && c.Status == ProfileClaimStatus.Pending)
            .Include(c => c.Claimant)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
        ViewBag.PendingClaims = pendingClaims;
        ViewBag.MyPendingClaim = pendingClaims.FirstOrDefault(c => c.ClaimantUserId == userId);

        // Is this viewer eligible to file a NEW claim? Claimable bar +
        // not creator + not already linked + no existing claim of their
        // own + visibility-checked (already done above).
        var isClaimable = profile.BirthYear.HasValue
                          && !string.IsNullOrWhiteSpace(profile.BirthPlace);
        ViewBag.IsClaimable = isClaimable;
        ViewBag.CanFileClaim = !isOwner
                            && string.IsNullOrEmpty(profile.LinkedUserId)
                            && isClaimable
                            && ViewBag.MyPendingClaim == null;

        // Family-tree wiring: does this profile already have a node in
        // the creator's tree? If yes, surface a "view on family tree"
        // link. If not, expose the list of existing tree nodes so the
        // creator can pick an anchor + relation and drop this NPC into
        // the tree without bouncing to the family-tree page first.
        if (isOwner)
        {
            ViewBag.OnFamilyTree = await _db.FamilyTreeNodes.AnyAsync(n =>
                n.OwnerUserId == userId
                && n.NodeKind == FamilyNodeKind.Profile
                && n.TargetProfileId == profile.Id);
            if (!(bool)ViewBag.OnFamilyTree)
            {
                var treeNodes = await _db.FamilyTreeNodes
                    .Where(n => n.OwnerUserId == userId)
                    .Include(n => n.TargetUser)
                    .Include(n => n.TargetProfile)
                    .ToListAsync();
                ViewBag.TreeAnchors = treeNodes
                    .Select(n => new
                    {
                        n.Id,
                        Label = n.NodeKind == FamilyNodeKind.Profile
                            ? (n.TargetProfile?.DisplayName ?? "(missing profile)")
                            : (n.TargetUser?.DisplayName ?? n.TargetUser?.UserName ?? "(missing member)")
                    })
                    .OrderBy(x => x.Label)
                    .ToList();
            }
        }

        ViewBag.IsOwner = isOwner;
        ViewBag.TaggedInPosts = visibleTagged;
        ViewBag.PhotoTagPostGroups = visiblePhotoPostGroups;

        // Parents and kids per the family-tree graph. We look at every
        // FamilyTreeNode that points at THIS profile — across all trees
        // the viewer can see (their personal tree, plus group trees
        // they're a member of) — and from there walk Parent edges in
        // both directions. Deduped by target person so the same Bob
        // doesn't show up twice when he's on multiple surfaces.
        // Permission-scoped so private branches don't leak.
        var profileNodeIds = await _db.FamilyTreeNodes
            .Where(n => n.NodeKind == FamilyNodeKind.Profile
                     && n.TargetProfileId == profile.Id
                     && (n.OwnerUserId == userId
                         || _db.FamilyGroupMembers.Any(m => m.UserId == userId
                                                          && m.FamilyGroupId == n.FamilyGroupId)))
            .Select(n => n.Id)
            .ToListAsync();
        var parentNodeIds = new List<int>();
        var childNodeIds  = new List<int>();
        var spouseNodeIds = new List<int>();
        if (profileNodeIds.Count > 0)
        {
            parentNodeIds = await _db.FamilyRelationships
                .Where(r => r.RelType == FamilyRelationType.Parent
                         && profileNodeIds.Contains(r.ToNodeId))
                .Select(r => r.FromNodeId)
                .Distinct()
                .ToListAsync();
            childNodeIds = await _db.FamilyRelationships
                .Where(r => r.RelType == FamilyRelationType.Parent
                         && profileNodeIds.Contains(r.FromNodeId))
                .Select(r => r.ToNodeId)
                .Distinct()
                .ToListAsync();
            // Spouse is symmetric — find the OTHER end of every Spouse
            // edge that touches a node pointing at this profile.
            spouseNodeIds = await _db.FamilyRelationships
                .Where(r => r.RelType == FamilyRelationType.Spouse
                         && (profileNodeIds.Contains(r.FromNodeId) || profileNodeIds.Contains(r.ToNodeId)))
                .Select(r => profileNodeIds.Contains(r.FromNodeId) ? r.ToNodeId : r.FromNodeId)
                .Distinct()
                .ToListAsync();
        }
        var allLinkedIds = parentNodeIds.Concat(childNodeIds).Concat(spouseNodeIds).Distinct().ToList();
        var linkedNodes = allLinkedIds.Count == 0
            ? new List<FamilyTreeNode>()
            : await _db.FamilyTreeNodes
                .Where(n => allLinkedIds.Contains(n.Id))
                .Include(n => n.TargetUser)
                .Include(n => n.TargetProfile)
                .ToListAsync();
        // Reduce to (label, link target) per distinct underlying person.
        (string Label, string? UserLink, int? ProfileLink) Resolve(FamilyTreeNode n) =>
            n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null
                ? (n.TargetUser.DisplayName ?? n.TargetUser.UserName ?? "(member)", n.TargetUser.Id, (int?)null)
                : (n.TargetProfile?.DisplayName ?? "(missing)", null, n.TargetProfileId);
        ViewBag.FamilyParents = linkedNodes
            .Where(n => parentNodeIds.Contains(n.Id))
            .Select(Resolve)
            .GroupBy(x => (x.UserLink, x.ProfileLink))
            .Select(g => g.First())
            .ToList();
        ViewBag.FamilyChildren = linkedNodes
            .Where(n => childNodeIds.Contains(n.Id))
            .Select(Resolve)
            .GroupBy(x => (x.UserLink, x.ProfileLink))
            .Select(g => g.First())
            .ToList();
        ViewBag.FamilySpouses = linkedNodes
            .Where(n => spouseNodeIds.Contains(n.Id))
            .Select(Resolve)
            .GroupBy(x => (x.UserLink, x.ProfileLink))
            .Select(g => g.First())
            .ToList();

        // Relationship-arc milestones for the Friendship graph editor.
        // Wrapped in try/catch so a missing-table situation (e.g. on a
        // stale deploy before the migration has run) doesn't blow up
        // the whole Details page.
        var milestones = new List<ProfileMilestone>();
        try
        {
            milestones = await _db.ProfileMilestones
                .Where(m => m.PersonProfileId == profile.Id)
                .OrderBy(m => m.Year)
                .ThenBy(m => m.Id)
                .ToListAsync();
        }
        catch { /* silently fall back to empty */ }
        ViewBag.Milestones = milestones;

        return View(profile);
    }

    // POST: /PersonProfiles/AddMilestone — append a milestone to the
    // profile's relationship arc. Creator-only.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMilestone(int id, int year, ProfileMilestoneKind kind, string? note)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (!await CanEditProfileAsync(profile, userId)) return Forbid();
        if (year < 1 || year > 2100)
        {
            TempData["Error"] = "Year must be between 1 and 2100.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var milestone = new ProfileMilestone
        {
            PersonProfileId = id,
            Year = year,
            Kind = kind,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.ProfileMilestones.Add(milestone);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Milestone added.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PersonProfiles/DeleteMilestone
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMilestone(int id, int milestoneId)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (!await CanEditProfileAsync(profile, userId)) return Forbid();
        var milestone = await _db.ProfileMilestones
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.PersonProfileId == id);
        if (milestone == null) return NotFound();
        _db.ProfileMilestones.Remove(milestone);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Milestone removed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Maps a milestone kind to its closeness band on the
    /// Friendship graph. Each band has exactly one kind, so the chart
    /// labels and the dropdown labels read identically.
    /// Bands: +3 Best · +2 Close · +1 Friend · 0 Connected ·
    /// -1 Drifted · -2 Estranged · -3 Lost contact.</summary>
    public static int ClosenessLevel(ProfileMilestoneKind k) => k switch
    {
        ProfileMilestoneKind.Best        =>  3,
        ProfileMilestoneKind.Close       =>  2,
        ProfileMilestoneKind.Friend      =>  1,
        ProfileMilestoneKind.Connected   =>  0,
        ProfileMilestoneKind.Drifted     => -1,
        ProfileMilestoneKind.Estranged   => -2,
        ProfileMilestoneKind.LostContact => -3,
        _ => 0
    };

    // GET: /PersonProfiles/Friendship — the relationship arc graph
    // across all non-Family profiles the user has created. Each profile
    // is a step-function line; milestones drive the steps; the bars
    // underneath (Round 3) show story-tag intensity per year.
    public async Task<IActionResult> Friendship()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);
        if (!await _premium.IsAvailableAsync(user, PremiumFeature.PeopleProfiles))
        {
            TempData["Info"] = "People profiles aren't available right now.";
            return RedirectToAction("Index", "Home");
        }

        var profiles = await _db.PersonProfiles
            .Where(p => p.CreatorUserId == userId && p.Kind != PersonProfileKind.Family)
            .OrderBy(p => p.DisplayName)
            .ToListAsync();

        var profileIds = profiles.Select(p => p.Id).ToList();

        // Wrapped: if the ProfileMilestones table isn't there yet (stale
        // deploy, migration still pending), fall back to an empty list
        // so the page still renders the friends with no milestones
        // rather than 500ing.
        var milestones = new List<ProfileMilestone>();
        if (profileIds.Count > 0)
        {
            try
            {
                milestones = await _db.ProfileMilestones
                    .Where(m => profileIds.Contains(m.PersonProfileId))
                    .OrderBy(m => m.Year)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Milestones couldn't be loaded — the database may still be migrating. " + ex.GetBaseException().Message;
            }
        }

        // Story-tag intensity per profile per year (Round 3). We use
        // EventYear (the year the story is *about*) rather than
        // CreatedAt so the bars line up with the milestone timeline.
        // Posts tagged with multiple profiles count for each. Wrapped
        // because the tag column has historically had a few rough
        // edges (stray commas, ids of deleted profiles) we don't want
        // to take the whole page down.
        var tagCounts = new Dictionary<int, Dictionary<int, int>>();
        if (profileIds.Count > 0)
        {
            try
            {
                var tagPattern = profileIds.ToHashSet();
                var posts = await _db.LifeEventPosts
                    .Where(p => !p.IsDraft
                                && p.TaggedProfileIds != null
                                && p.TaggedProfileIds != ""
                                && p.OwnerUserId == userId)
                    .Select(p => new { p.EventYear, p.TaggedProfileIds })
                    .ToListAsync();
                foreach (var p in posts)
                {
                    if (p.EventYear <= 0) continue;
                    var tagged = (p.TaggedProfileIds ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                        .Where(n => n > 0 && tagPattern.Contains(n));
                    foreach (var pid in tagged)
                    {
                        if (!tagCounts.TryGetValue(pid, out var perYear))
                        {
                            perYear = new Dictionary<int, int>();
                            tagCounts[pid] = perYear;
                        }
                        perYear[p.EventYear] = perYear.GetValueOrDefault(p.EventYear) + 1;
                    }
                }
            }
            catch { /* best-effort — bars are decorative */ }
        }

        var series = profiles.Select(p =>
        {
            var ms = milestones.Where(m => m.PersonProfileId == p.Id).OrderBy(m => m.Year).ToList();
            return new
            {
                id = p.Id,
                name = string.IsNullOrWhiteSpace(p.Nickname) ? p.DisplayName : p.Nickname,
                kind = p.Kind.ToString(),
                metYear = p.MetYear,
                milestones = ms.Select(m => new
                {
                    year = m.Year,
                    kind = m.Kind.ToString(),
                    level = ClosenessLevel(m.Kind),
                    note = m.Note
                }).ToList(),
                tagsByYear = tagCounts.TryGetValue(p.Id, out var ty)
                    ? ty.OrderBy(kv => kv.Key).Select(kv => new { year = kv.Key, count = kv.Value }).ToList<object>()
                    : new List<object>()
            };
        }).ToList();

        ViewBag.SeriesJson = System.Text.Json.JsonSerializer.Serialize(series);
        ViewBag.ProfileCount = profiles.Count;
        return View(profiles);
    }

    // POST: /PersonProfiles/CreatorLink/5 — the creator directly links
    // an NPC profile to a member they're already connected to. This is
    // Tier 1 of the claim authority hierarchy: the creator made the
    // card and recognises the new member as the real person — no
    // disambiguation, no joiner round-trip required.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatorLink(int id, string targetUserId)
    {
        var userId = _userManager.GetUserId(User)!;
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (profile.CreatorUserId != userId && !User.IsInRole("Admin")) return Forbid();
        if (!string.IsNullOrEmpty(profile.LinkedUserId))
        {
            TempData["Error"] = "This profile is already linked to a member.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (string.IsNullOrWhiteSpace(targetUserId) || targetUserId == userId)
        {
            TempData["Error"] = "Pick a different member to link this profile to.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Authorise: target must be in the creator's friend list. Stops
        // a creator from linking a profile to an arbitrary stranger's
        // account.
        var friendList = await _friends.GetFriendListAsync(userId);
        var target = friendList.Friends.FirstOrDefault(f => f.User.Id == targetUserId)?.User;
        if (target == null)
        {
            TempData["Error"] = "You can only link to someone in your network.";
            return RedirectToAction(nameof(Details), new { id });
        }

        profile.LinkedUserId    = target.Id;
        profile.ClaimedAt       = DateTime.UtcNow;
        profile.ClaimDeclinedAt = null;
        profile.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Mirror the creator's family-tree neighbourhood around this
        // NPC into the linked member's tree, so they don't start from
        // an empty canvas. Failure here doesn't roll back the link.
        try { await AutoFillJoinerTreeAsync(profile.Id, target.Id); }
        catch { /* tree fill is best-effort */ }

        // Notify the linked member — they may want to know an NPC card
        // about them is now routed through their timeline, and they
        // have the option to unlink if they don't want the association.
        var creatorName = (await _userManager.GetUserAsync(User))?.DisplayName ?? "Someone";
        await _notifications.CreateAsync(
            target.Id,
            NotificationType.ProfileClaimed,
            $"{creatorName} linked the profile they created for {profile.DisplayName} to your account.",
            Url.Action(nameof(Details), "PersonProfiles", new { id = profile.Id }),
            userId);

        TempData["Success"] = $"Linked profile for {profile.DisplayName} to {target.DisplayName ?? target.UserName}. Tags now route to their timeline.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public class MediaPersonTagGroup
    {
        public LifeEventPost Post { get; set; } = null!;
        public List<MediaPersonTag> Tags { get; set; } = new();
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

        // Mirror the creator's family-tree neighbourhood around this
        // NPC into the joiner's own tree. Best-effort.
        try { await AutoFillJoinerTreeAsync(profile.Id, user.Id); }
        catch { /* tree fill is best-effort */ }

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

    // Mirror the creator's family-tree neighbourhood around a linked
    // PersonProfile into the joiner's own tree. Walks the reachable
    // subgraph from the creator's NPC node via Parent/Spouse/Sibling
    // edges, materialising equivalent nodes + edges in joiner's tree.
    //
    // Idempotent: rerunning is a no-op. Reuses joiner's existing nodes
    // when they already reference the same TargetUserId / TargetProfileId.
    // The creator's tree is never modified. Wrapped in try/catch by
    // each caller — auto-fill failures shouldn't block the link itself.
    private async Task AutoFillJoinerTreeAsync(int profileId, string joinerUserId)
    {
        var creatorNodes = await _db.FamilyTreeNodes
            .Where(n => n.NodeKind == FamilyNodeKind.Profile
                        && n.TargetProfileId == profileId
                        && n.OwnerUserId != joinerUserId)
            .ToListAsync();
        foreach (var startNode in creatorNodes)
        {
            await MirrorTreeNeighbourhoodAsync(startNode, joinerUserId);
        }
    }

    private async Task MirrorTreeNeighbourhoodAsync(FamilyTreeNode startNode, string joinerUserId)
    {
        var creatorUserId = startNode.OwnerUserId;
        if (creatorUserId == joinerUserId) return;

        var creatorNodes = await _db.FamilyTreeNodes
            .Where(n => n.OwnerUserId == creatorUserId)
            .ToListAsync();
        var creatorEdges = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == creatorUserId)
            .ToListAsync();
        var joinerNodes = await _db.FamilyTreeNodes
            .Where(n => n.OwnerUserId == joinerUserId)
            .ToListAsync();

        // Auto-seed self for the joiner if their tree is empty.
        var joinerSelfNode = joinerNodes.FirstOrDefault(n =>
            n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == joinerUserId);
        if (joinerSelfNode == null)
        {
            joinerSelfNode = new FamilyTreeNode
            {
                OwnerUserId = joinerUserId,
                NodeKind = FamilyNodeKind.Member,
                TargetUserId = joinerUserId
            };
            _db.FamilyTreeNodes.Add(joinerSelfNode);
            await _db.SaveChangesAsync();
            joinerNodes.Add(joinerSelfNode);
        }

        // BFS from startNode across the creator's tree via family edges.
        // The reachable subgraph is everyone the joiner could trace a
        // family path to from their NPC card — i.e. their relatives.
        var creatorById = creatorNodes.ToDictionary(n => n.Id);
        var reachable = new HashSet<int> { startNode.Id };
        var queue = new Queue<int>();
        queue.Enqueue(startNode.Id);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var e in creatorEdges.Where(x => x.FromNodeId == cur || x.ToNodeId == cur))
            {
                var other = e.FromNodeId == cur ? e.ToNodeId : e.FromNodeId;
                if (reachable.Add(other)) queue.Enqueue(other);
            }
        }

        // Map creator's node id → joiner's tree node (existing or newly
        // created). The creator's start node maps to the joiner's self
        // (the linked profile IS them); other Member references reuse
        // the same TargetUserId; other Profile references stay attached
        // to the same PersonProfile row (no NPC duplication).
        var map = new Dictionary<int, FamilyTreeNode> { [startNode.Id] = joinerSelfNode };
        foreach (var cid in reachable)
        {
            if (map.ContainsKey(cid)) continue;
            var cN = creatorById[cid];
            FamilyTreeNode? jN = null;
            if (cN.NodeKind == FamilyNodeKind.Member && !string.IsNullOrEmpty(cN.TargetUserId))
            {
                if (cN.TargetUserId == joinerUserId)
                {
                    jN = joinerSelfNode;
                }
                else
                {
                    jN = joinerNodes.FirstOrDefault(n =>
                        n.NodeKind == FamilyNodeKind.Member && n.TargetUserId == cN.TargetUserId);
                    if (jN == null)
                    {
                        jN = new FamilyTreeNode
                        {
                            OwnerUserId = joinerUserId,
                            NodeKind = FamilyNodeKind.Member,
                            TargetUserId = cN.TargetUserId
                        };
                        _db.FamilyTreeNodes.Add(jN);
                        joinerNodes.Add(jN);
                    }
                }
            }
            else if (cN.NodeKind == FamilyNodeKind.Profile && cN.TargetProfileId.HasValue)
            {
                jN = joinerNodes.FirstOrDefault(n =>
                    n.NodeKind == FamilyNodeKind.Profile && n.TargetProfileId == cN.TargetProfileId);
                if (jN == null)
                {
                    jN = new FamilyTreeNode
                    {
                        OwnerUserId = joinerUserId,
                        NodeKind = FamilyNodeKind.Profile,
                        TargetProfileId = cN.TargetProfileId
                    };
                    _db.FamilyTreeNodes.Add(jN);
                    joinerNodes.Add(jN);
                }
            }
            if (jN != null) map[cid] = jN;
        }
        await _db.SaveChangesAsync();

        // Mirror edges. Dedup by (RelType, FromJoinerId, ToJoinerId),
        // treating Spouse and Sibling as symmetric.
        var joinerEdges = await _db.FamilyRelationships
            .Where(r => r.OwnerUserId == joinerUserId)
            .ToListAsync();
        foreach (var e in creatorEdges)
        {
            if (!reachable.Contains(e.FromNodeId) || !reachable.Contains(e.ToNodeId)) continue;
            if (!map.TryGetValue(e.FromNodeId, out var fromJ)) continue;
            if (!map.TryGetValue(e.ToNodeId,   out var toJ))   continue;
            if (fromJ.Id == toJ.Id) continue;
            var sym = e.RelType == FamilyRelationType.Spouse
                   || e.RelType == FamilyRelationType.Sibling;
            bool exists = joinerEdges.Any(j =>
                j.RelType == e.RelType
                && ((j.FromNodeId == fromJ.Id && j.ToNodeId == toJ.Id)
                    || (sym && j.FromNodeId == toJ.Id && j.ToNodeId == fromJ.Id)));
            if (exists) continue;
            var newEdge = new FamilyRelationship
            {
                OwnerUserId = joinerUserId,
                FromNodeId = fromJ.Id,
                ToNodeId = toJ.Id,
                RelType = e.RelType
            };
            _db.FamilyRelationships.Add(newEdge);
            joinerEdges.Add(newEdge);
        }
        await _db.SaveChangesAsync();
    }

    // POST: /PersonProfiles/RequestClaim/5 — joiner files a claim
    // request on an NPC profile they believe is them. Goes into the
    // queue for the creator to approve/deny. Tier 2/3 of the claim
    // authority hierarchy.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestClaim(int id, string? note)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile == null) return NotFound();
        if (!string.IsNullOrEmpty(profile.LinkedUserId))
        {
            TempData["Error"] = "This profile is already linked to a member.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (profile.CreatorUserId == user.Id)
        {
            TempData["Error"] = "You created this profile — you can't file a claim on it.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Visibility gate — same ladder Details uses. If the user can't
        // see the profile they shouldn't be able to claim it.
        if (profile.Visibility != PostVisibility.Public)
        {
            var canSee = await _permissions.CanViewPostsAsync(user.Id, profile.CreatorUserId);
            if (!canSee) return Forbid();
            var tier = await _permissions.GetViewerTierAsync(user.Id, profile.CreatorUserId);
            if (profile.Visibility == PostVisibility.Family && tier != FriendTier.Family) return Forbid();
            if (profile.Visibility == PostVisibility.Friends
                && tier != FriendTier.Friend && tier != FriendTier.Family) return Forbid();
        }

        // Claimable threshold: NPC must carry full name + birth year +
        // birthplace so the creator has enough signal to disambiguate.
        // Below this bar, the profile shouldn't surface in joiner-claim
        // workflows — falls back to creator-initiated linking (PR 1).
        if (!profile.BirthYear.HasValue || string.IsNullOrWhiteSpace(profile.BirthPlace))
        {
            TempData["Error"] = "This profile is missing a birth year or birthplace — it can't be claimed until the creator fills those in.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var existing = await _db.ProfileClaims.FirstOrDefaultAsync(c =>
            c.PersonProfileId == id
            && c.ClaimantUserId == user.Id
            && (c.Status == ProfileClaimStatus.Pending || c.Status == ProfileClaimStatus.Approved));
        if (existing != null)
        {
            TempData["Error"] = existing.Status == ProfileClaimStatus.Pending
                ? "You already have a pending claim on this profile."
                : "You've already claimed this profile.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var claim = new ProfileClaim
        {
            PersonProfileId = id,
            ClaimantUserId = user.Id,
            Status = ProfileClaimStatus.Pending,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.ProfileClaims.Add(claim);
        await _db.SaveChangesAsync();

        var claimerName = user.DisplayName ?? user.UserName ?? "Someone";
        await _notifications.CreateAsync(
            profile.CreatorUserId,
            NotificationType.ProfileClaimRequested,
            $"{claimerName} says they're the real person behind {profile.DisplayName}. Review the claim.",
            Url.Action(nameof(Details), "PersonProfiles", new { id = profile.Id }),
            user.Id);

        TempData["Success"] = $"Claim sent to the creator of {profile.DisplayName}. You'll hear back when they review it.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PersonProfiles/ApproveClaim/123 — creator approves a
    // pending claim. Sets LinkedUserId, denies any other pending
    // claims on the same profile, and notifies the claimant.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveClaim(int claimId)
    {
        var userId = _userManager.GetUserId(User)!;
        var claim = await _db.ProfileClaims
            .Include(c => c.PersonProfile)
            .Include(c => c.Claimant)
            .FirstOrDefaultAsync(c => c.Id == claimId);
        if (claim == null || claim.PersonProfile == null) return NotFound();
        if (claim.PersonProfile.CreatorUserId != userId && !User.IsInRole("Admin")) return Forbid();
        if (claim.Status != ProfileClaimStatus.Pending)
        {
            TempData["Error"] = "This claim is no longer pending.";
            return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
        }
        if (!string.IsNullOrEmpty(claim.PersonProfile.LinkedUserId))
        {
            TempData["Error"] = "This profile is already linked to a member.";
            return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
        }

        var now = DateTime.UtcNow;
        claim.Status = ProfileClaimStatus.Approved;
        claim.ResolvedAt = now;
        claim.PersonProfile.LinkedUserId = claim.ClaimantUserId;
        claim.PersonProfile.ClaimedAt = now;
        claim.PersonProfile.ClaimDeclinedAt = null;
        claim.PersonProfile.UpdatedAt = now;

        // Auto-deny any other pending claims on this same profile —
        // only one person can be the real one.
        var siblings = await _db.ProfileClaims
            .Where(c => c.PersonProfileId == claim.PersonProfileId
                        && c.Id != claim.Id
                        && c.Status == ProfileClaimStatus.Pending)
            .ToListAsync();
        foreach (var s in siblings)
        {
            s.Status = ProfileClaimStatus.Denied;
            s.ResolvedAt = now;
        }
        await _db.SaveChangesAsync();

        // Mirror the creator's family-tree neighbourhood around this
        // NPC into the claimant's own tree. Best-effort.
        try { await AutoFillJoinerTreeAsync(claim.PersonProfileId, claim.ClaimantUserId); }
        catch { /* tree fill is best-effort */ }

        await _notifications.CreateAsync(
            claim.ClaimantUserId,
            NotificationType.ProfileClaimApproved,
            $"Your claim on {claim.PersonProfile.DisplayName} was approved. Tags now route to your timeline.",
            Url.Action(nameof(Details), "PersonProfiles", new { id = claim.PersonProfileId }),
            userId);
        foreach (var s in siblings)
        {
            await _notifications.CreateAsync(
                s.ClaimantUserId,
                NotificationType.ProfileClaimDenied,
                $"Another claim was approved on {claim.PersonProfile.DisplayName} — yours was closed.",
                Url.Action(nameof(Details), "PersonProfiles", new { id = claim.PersonProfileId }),
                userId);
        }

        var claimantName = claim.Claimant?.DisplayName ?? claim.Claimant?.UserName ?? "the claimant";
        TempData["Success"] = $"Approved {claimantName}'s claim. Profile is now linked to their account.";
        return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
    }

    // POST: /PersonProfiles/DenyClaim/123 — creator denies a pending
    // claim. The joiner gets a notification; no link is created.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DenyClaim(int claimId)
    {
        var userId = _userManager.GetUserId(User)!;
        var claim = await _db.ProfileClaims
            .Include(c => c.PersonProfile)
            .Include(c => c.Claimant)
            .FirstOrDefaultAsync(c => c.Id == claimId);
        if (claim == null || claim.PersonProfile == null) return NotFound();
        if (claim.PersonProfile.CreatorUserId != userId && !User.IsInRole("Admin")) return Forbid();
        if (claim.Status != ProfileClaimStatus.Pending)
        {
            TempData["Error"] = "This claim is no longer pending.";
            return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
        }

        claim.Status = ProfileClaimStatus.Denied;
        claim.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifications.CreateAsync(
            claim.ClaimantUserId,
            NotificationType.ProfileClaimDenied,
            $"Your claim on {claim.PersonProfile.DisplayName} was declined by the creator.",
            Url.Action(nameof(Details), "PersonProfiles", new { id = claim.PersonProfileId }),
            userId);

        TempData["Success"] = "Claim denied.";
        return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
    }

    // POST: /PersonProfiles/WithdrawClaim/123 — claimant cancels their
    // own pending claim.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> WithdrawClaim(int claimId)
    {
        var userId = _userManager.GetUserId(User)!;
        var claim = await _db.ProfileClaims.FirstOrDefaultAsync(c => c.Id == claimId);
        if (claim == null) return NotFound();
        if (claim.ClaimantUserId != userId) return Forbid();
        if (claim.Status != ProfileClaimStatus.Pending)
        {
            TempData["Error"] = "This claim is no longer pending.";
            return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
        }
        claim.Status = ProfileClaimStatus.Withdrawn;
        claim.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Claim withdrawn.";
        return RedirectToAction(nameof(Details), new { id = claim.PersonProfileId });
    }
}
