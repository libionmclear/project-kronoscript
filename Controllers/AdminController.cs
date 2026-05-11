using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAccountDeletionService _deletion;
    private readonly ISiteSettings _siteSettings;

    public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAccountDeletionService deletion, ISiteSettings siteSettings)
    {
        _db = db;
        _userManager = userManager;
        _deletion = deletion;
        _siteSettings = siteSettings;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);
        var totalUsers = await _db.Users.CountAsync();
        var newUsersThisWeek = await _db.Users.CountAsync(u => u.CreatedAt >= sevenDaysAgo);

        // Active in last 30 days = users who posted in that period
        var activeUsersLast30Days = await _db.LifeEventPosts
            .Where(p => p.CreatedAt >= thirtyDaysAgo)
            .Select(p => p.OwnerUserId)
            .Distinct()
            .CountAsync();

        // Active now = posted in last 24 hours (no session tracking without LastActivityAt)
        var activeUsersNow = await _db.LifeEventPosts
            .Where(p => p.CreatedAt >= now.AddHours(-24))
            .Select(p => p.OwnerUserId)
            .Distinct()
            .CountAsync();

        var totalPosts = await _db.LifeEventPosts.CountAsync();
        var totalComments = await _db.Comments.CountAsync();
        var totalLikes = await _db.PostLikes.CountAsync();

        int activeBans = 0, permanentBans = 0;
        try
        {
            activeBans = await _db.UserBans.CountAsync(b =>
                b.BanType == BanType.Temporary &&
                (b.BanExpiry == null || b.BanExpiry > now));
            permanentBans = await _db.UserBans.CountAsync(b => b.BanType == BanType.Permanent);
        }
        catch { /* UserBans table may not exist yet */ }

        var vm = new AdminDashboardViewModel
        {
            TotalUsers = totalUsers,
            ActiveUsersLast30Days = activeUsersLast30Days,
            ActiveUsersNow = activeUsersNow,
            NewUsersThisWeek = newUsersThisWeek,
            TotalPosts = totalPosts,
            TotalComments = totalComments,
            TotalLikes = totalLikes,
            ActiveBans = activeBans,
            PermanentBans = permanentBans
        };

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> PromoteToAdmin(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) { TempData["Error"] = "User not found."; return RedirectToAction(nameof(Users)); }
        if (await _userManager.IsInRoleAsync(user, "Admin")) { TempData["Error"] = "User is already an admin."; return RedirectToAction(nameof(Users)); }
        await _userManager.AddToRoleAsync(user, "Admin");
        TempData["Success"] = $"{user.UserName} is now an assistant admin.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DemoteAdmin(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) { TempData["Error"] = "User not found."; return RedirectToAction(nameof(Users)); }
        if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
        {
            TempData["Error"] = "Super admins cannot be demoted from this page.";
            return RedirectToAction(nameof(Users));
        }
        if (!await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Error"] = "User is not an admin.";
            return RedirectToAction(nameof(Users));
        }
        await _userManager.RemoveFromRoleAsync(user, "Admin");
        TempData["Success"] = $"{user.UserName} is no longer an admin.";
        return RedirectToAction(nameof(Users));
    }

    // Admin-initiated password reset. We no longer generate a password
    // and flash it back through TempData (visible in browser history,
    // logs, screenshots) — instead we email the user a reset link, the
    // same one they'd get from the self-service ForgotPassword flow.
    // The admin's only role is "trigger the email."
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUserPassword(
        string userId,
        [FromServices] Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["Error"] = "Missing user.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.Email))
        {
            TempData["Error"] = "User not found or has no email on file.";
            return RedirectToAction(nameof(Users));
        }

        try
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
            var callbackUrl = Url.Action("ResetPassword", "Account",
                new { email = user.Email, token = encodedToken }, protocol: Request.Scheme)!;

            var body = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(user.DisplayName ?? user.UserName ?? "")},</p>
                          <p>An administrator triggered a password reset on your Kronoscript account. If you weren't expecting this, you can ignore this email — your current password still works until you click the link.</p>
                          <p><a href='{callbackUrl}' style='background:#1e4d2e;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block;'>Reset password</a></p>
                          <p>This link expires in 24 hours.</p>
                          <p style='color:#888;font-size:12px;'>— The Kronoscript Team</p>";

            await emailSender.SendEmailAsync(user.Email!, "Reset your Kronoscript password", body);
            TempData["Success"] = $"Password reset email sent to {user.Email}. They have 24 hours to follow the link.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Could not send reset email: " + ex.Message;
        }
        return RedirectToAction(nameof(Users));
    }

    public async Task<IActionResult> Users(string? search = null)
    {
        var now = DateTime.UtcNow;

        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u =>
                (u.UserName != null && u.UserName.Contains(search)) ||
                (u.Email != null && u.Email.Contains(search)) ||
                (u.DisplayName != null && u.DisplayName.Contains(search)));

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var adminIds = adminUsers.Select(u => u.Id).ToHashSet();
        var superAdminUsers = await _userManager.GetUsersInRoleAsync("SuperAdmin");
        var superAdminIds = superAdminUsers.Select(u => u.Id).ToHashSet();
        ViewBag.ViewerIsSuperAdmin = User.IsInRole("SuperAdmin");

        var postCounts = await _db.LifeEventPosts
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        Dictionary<string, UserBan> bansByUser = new();
        try
        {
            var activeBans = await _db.UserBans
                .Where(b => b.UserId != null &&
                            (b.BanType == BanType.Permanent ||
                             (b.BanExpiry != null && b.BanExpiry > now)))
                .ToListAsync();
            bansByUser = activeBans.ToDictionary(b => b.UserId!, b => b);
        }
        catch { /* UserBans table may not exist yet */ }

        // Signup ordinal across the whole user table — #1 is the first ever
        // signup. Built from a single oldest-first projection so the index
        // matches even if the current page is filtered/sorted differently.
        var ordinalIndex = (await _db.Users
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .ToListAsync())
            .Select((id, i) => new { id, i })
            .ToDictionary(x => x.id, x => x.i + 1);

        var vms = users.Select(u => new AdminUserViewModel
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            Email = u.Email ?? "",
            DisplayName = u.DisplayName,
            FirstName = u.FirstName,
            LastName = u.LastName,
            CreatedAt = u.CreatedAt,
            PostCount = postCounts.TryGetValue(u.Id, out var pc) ? pc : 0,
            IsAdmin = adminIds.Contains(u.Id),
            IsSuperAdmin = superAdminIds.Contains(u.Id),
            ActiveBan = bansByUser.TryGetValue(u.Id, out var ban) ? ban : null,
            Ordinal = ordinalIndex.TryGetValue(u.Id, out var n) ? n : 0,
            PremiumUntil = u.PremiumUntil,
            PremiumTier = u.PremiumTier,
            EmailConfirmed = u.EmailConfirmed,
            LockoutEnd = u.LockoutEnd
        }).ToList();

        ViewBag.Search = search;
        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BanUser(string userId, string? reason)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var adminId = _userManager.GetUserId(User);

        // Remove any existing temporary ban before adding a new one
        var existing = await _db.UserBans
            .Where(b => b.UserId == userId && b.BanType == BanType.Temporary)
            .ToListAsync();
        _db.UserBans.RemoveRange(existing);

        _db.UserBans.Add(new UserBan
        {
            UserId = userId,
            BannedEmail = user.Email!,
            BanType = BanType.Temporary,
            BannedAt = DateTime.UtcNow,
            BanExpiry = DateTime.UtcNow.AddDays(30),
            BannedByUserId = adminId,
            Reason = reason
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = $"User @{user.UserName} has been banned for 30 days.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnbanUser(int banId)
    {
        var ban = await _db.UserBans.FindAsync(banId);
        if (ban != null)
        {
            _db.UserBans.Remove(ban);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Ban removed.";
        }
        return RedirectToAction(nameof(Users));
    }

    // ── Pending signups — dedicated rescue page ────────────────────────
    // Surfaces users who are stuck (email never confirmed, or account
    // locked from too many bad logins) so an admin can approve them
    // without scrolling the full Users list. The same per-row actions
    // ConfirmUserEmail / UnlockUser / ResendUserConfirmation live here
    // alongside the existing Users page.
    public async Task<IActionResult> PendingSignups()
    {
        var now = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        var unverified = await _db.Users
            .Where(u => !u.EmailConfirmed)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var locked = await _db.Users
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > nowOffset && u.EmailConfirmed)
            .OrderByDescending(u => u.LockoutEnd)
            .ToListAsync();

        ViewBag.Unverified = unverified;
        ViewBag.Locked = locked;
        return View();
    }

    // ── Stuck-signup rescue: confirm email, unlock, resend confirmation ───

    /// <summary>Manually flip EmailConfirmed=true on a user who got stuck
    /// because the verification email never arrived. Use sparingly — this
    /// bypasses the email-ownership proof.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmUserEmail(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();
        if (user.EmailConfirmed)
        {
            TempData["Info"] = $"{user.UserName}'s email was already confirmed.";
            return RedirectToAction(nameof(Users));
        }
        user.EmailConfirmed = true;
        var result = await _userManager.UpdateAsync(user);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Marked {user.UserName}'s email as confirmed. They can now sign in."
            : "Could not confirm email: " + string.Join("; ", result.Errors.Select(e => e.Description));
        return RedirectToAction(nameof(Users));
    }

    /// <summary>Clear LockoutEnd + AccessFailedCount + RecentLockoutCount
    /// so a user who locked themselves out can try again immediately.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockUser(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        user.RecentLockoutCount = 0;
        await _userManager.UpdateAsync(user);
        TempData["Success"] = $"{user.UserName} unlocked. They can sign in immediately.";
        return RedirectToAction(nameof(Users));
    }

    /// <summary>Re-send the verification email from admin (bypassing the
    /// anon rate limit). Useful when SendGrid was misconfigured at signup
    /// time and is now working again.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendUserConfirmation(string userId,
        [FromServices] Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();
        if (user.EmailConfirmed)
        {
            TempData["Info"] = $"{user.UserName}'s email is already confirmed.";
            return RedirectToAction(nameof(Users));
        }
        if (string.IsNullOrEmpty(user.Email))
        {
            TempData["Error"] = "User has no email address on file.";
            return RedirectToAction(nameof(Users));
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
        var url = Url.Action("ConfirmEmail", "Account",
            new { userId = user.Id, token = encoded }, protocol: Request.Scheme);

        var html = $@"<p>Hi {user.DisplayName ?? user.UserName},</p>
                      <p>Here's a fresh verification link for your Kronoscript account:</p>
                      <p><a href='{url}' style='background:#1e4d2e;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block;'>Verify my email</a></p>
                      <p style='color:#888;font-size:12px;'>— Kronoscript</p>";

        if (emailSender is MyStoryTold.Services.EmailSender es)
        {
            var diag = await es.SendDiagnosticAsync(user.Email, "Verify your Kronoscript account", html);
            TempData[diag.Success ? "Success" : "Error"] = diag.Success
                ? $"Verification email resent to {user.Email}."
                : $"SendGrid rejected the send (status {diag.StatusCode}). {diag.Message}";
        }
        else
        {
            await emailSender.SendEmailAsync(user.Email, "Verify your Kronoscript account", html);
            TempData["Success"] = $"Verification email resent to {user.Email}.";
        }
        return RedirectToAction(nameof(Users));
    }

    // ── Tips & Announcements ──────────────────────────────────────────────

    public async Task<IActionResult> Tips()
    {
        List<Tip> tips;
        try { tips = await _db.Tips.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync(); }
        catch { tips = new List<Tip>(); }
        return View(tips);
    }

    // ── Channels ──────────────────────────────────────────────────────────

    public async Task<IActionResult> Channels()
    {
        var channels = await _db.Channels
            .Include(c => c.Admin)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return View(channels);
    }

    [HttpGet]
    public IActionResult CreateChannel() => View(new Channel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateChannel(Channel input, string? adminEmailOrUsername)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            TempData["Error"] = "Channel name is required.";
            return View(input);
        }
        if (string.IsNullOrWhiteSpace(input.Slug))
        {
            input.Slug = SlugifyChannelName(input.Name);
        }
        else
        {
            input.Slug = SlugifyChannelName(input.Slug);
        }

        // Resolve the assigned admin (lookup by email or username).
        ApplicationUser? channelAdmin = null;
        if (!string.IsNullOrWhiteSpace(adminEmailOrUsername))
        {
            channelAdmin = await ResolveUserByHandleAsync(adminEmailOrUsername);
            if (channelAdmin == null)
            {
                TempData["Error"] = $"No user found for '{adminEmailOrUsername}'. Channel created with no assigned writer.";
            }
        }

        // Slug uniqueness — append a number if taken.
        var baseSlug = input.Slug;
        int n = 2;
        while (await _db.Channels.AnyAsync(c => c.Slug == input.Slug))
        {
            input.Slug = $"{baseSlug}-{n++}";
        }

        input.AdminUserId = channelAdmin?.Id;
        input.CreatedByUserId = _userManager.GetUserId(User)!;
        input.CreatedAt = DateTime.UtcNow;
        _db.Channels.Add(input);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Channel '{input.Name}' created.";
        return RedirectToAction(nameof(Channels));
    }

    [HttpGet]
    public async Task<IActionResult> EditChannel(int id)
    {
        var channel = await _db.Channels.Include(c => c.Admin).FirstOrDefaultAsync(c => c.Id == id);
        if (channel == null) return NotFound();
        ViewBag.AdminEmailOrUsername = channel.Admin?.Email ?? channel.Admin?.UserName;
        return View(channel);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditChannel(int id, Channel input, string? adminEmailOrUsername)
    {
        var channel = await _db.Channels.FirstOrDefaultAsync(c => c.Id == id);
        if (channel == null) return NotFound();

        channel.Name = string.IsNullOrWhiteSpace(input.Name) ? channel.Name : input.Name.Trim();
        channel.Description = input.Description?.Trim();
        channel.IconEmoji = input.IconEmoji?.Trim();
        channel.DefaultLayoutStyle = input.DefaultLayoutStyle;

        if (!string.IsNullOrWhiteSpace(input.Slug) && !string.Equals(input.Slug, channel.Slug, StringComparison.OrdinalIgnoreCase))
        {
            var newSlug = SlugifyChannelName(input.Slug);
            if (!await _db.Channels.AnyAsync(c => c.Id != id && c.Slug == newSlug))
            {
                channel.Slug = newSlug;
            }
            else
            {
                TempData["Error"] = "That slug is already in use; keeping the previous one.";
            }
        }

        // Resolve assigned writer. A blank field means "unassign". A non-blank
        // value that doesn't match any user is treated as a typo — keep the
        // existing assignment and surface the error so the writer doesn't
        // silently disappear when the admin misspells their handle.
        if (string.IsNullOrWhiteSpace(adminEmailOrUsername))
        {
            channel.AdminUserId = null;
        }
        else
        {
            var channelAdmin = await ResolveUserByHandleAsync(adminEmailOrUsername);
            if (channelAdmin != null)
            {
                channel.AdminUserId = channelAdmin.Id;
            }
            else
            {
                TempData["Error"] = $"No user found for '{adminEmailOrUsername}'. Existing writer assignment kept.";
            }
        }
        await _db.SaveChangesAsync();

        if (!TempData.ContainsKey("Error")) TempData["Success"] = "Channel saved.";
        return RedirectToAction(nameof(Channels));
    }

    /// <summary>Resolve a user by either email, username, or display name. The
    /// admin's "@" prefix is stripped so users typing the placeholder
    /// "@username" verbatim still find the right account.</summary>
    private async Task<ApplicationUser?> ResolveUserByHandleAsync(string handle)
    {
        var h = handle.Trim().TrimStart('@');
        if (h.Length == 0) return null;
        return await _userManager.FindByEmailAsync(h)
            ?? await _userManager.FindByNameAsync(h)
            ?? await _db.Users.FirstOrDefaultAsync(u => u.DisplayName == h);
    }

    /// <summary>JSON endpoint for the channel writer / managed-user autocomplete
    /// fields. Returns up to 10 matches whose username, email, or display name
    /// starts with (or contains) <paramref name="q"/>.</summary>
    [HttpGet]
    public async Task<IActionResult> SearchUsers(string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Json(Array.Empty<object>());
        }
        var needle = q.Trim().TrimStart('@').ToLowerInvariant();
        var hits = await _db.Users
            .Where(u => (u.UserName != null && u.UserName.ToLower().Contains(needle))
                     || (u.Email != null && u.Email.ToLower().Contains(needle))
                     || (u.DisplayName != null && u.DisplayName.ToLower().Contains(needle)))
            .OrderBy(u => u.UserName)
            .Take(10)
            .Select(u => new
            {
                userName = u.UserName,
                email = u.Email,
                displayName = u.DisplayName,
                isBiographical = u.IsBiographical
            })
            .ToListAsync();
        return Json(hits);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChannel(int id)
    {
        var channel = await _db.Channels.FindAsync(id);
        if (channel != null)
        {
            // Posts in this channel get their ChannelId set to null via the FK config; the posts stay.
            _db.Channels.Remove(channel);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Channel deleted; existing posts kept.";
        }
        return RedirectToAction(nameof(Channels));
    }

    private static string SlugifyChannelName(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == ' ' || ch == '-' || ch == '_') sb.Append('-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "channel" : slug;
    }

    // ── Biographical / managed accounts ───────────────────────────────────

    public async Task<IActionResult> ManagedUsers()
    {
        var meId = _userManager.GetUserId(User);
        // Admins see all managed accounts (so a vacationing admin can hand
        // one off); list shows owner so it's clear who's responsible.
        var users = await _db.Users
            .Where(u => u.ManagedByUserId != null || u.IsBiographical)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var ownerIds = users.Select(u => u.ManagedByUserId).Where(id => id != null).Distinct().ToList();
        var owners = await _db.Users.Where(u => ownerIds.Contains(u.Id)).ToListAsync();
        ViewBag.Owners = owners.ToDictionary(u => u.Id, u => u);
        return View(users);
    }

    [HttpGet]
    public IActionResult CreateManagedUser() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManagedUser(string userName, string displayName, string? era, string? summary, string? profilePhotoUrl)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            TempData["Error"] = "Username is required.";
            return RedirectToAction(nameof(CreateManagedUser));
        }
        userName = userName.Trim();

        if (await _userManager.FindByNameAsync(userName) != null)
        {
            TempData["Error"] = $"Username '{userName}' is already taken.";
            return RedirectToAction(nameof(CreateManagedUser));
        }

        var adminId = _userManager.GetUserId(User)!;
        var admin = await _userManager.FindByIdAsync(adminId);
        // Synthetic email so Identity's RequireUniqueEmail / RequireConfirmedEmail
        // don't fight us — the inbox is purely notional.
        var syntheticEmail = $"{userName.ToLowerInvariant()}+managed-{Guid.NewGuid():N}@kronoscript.managed";

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = syntheticEmail,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim(),
            ProfilePhotoUrl = string.IsNullOrWhiteSpace(profilePhotoUrl) ? null : profilePhotoUrl.Trim(),
            ManagedByUserId = adminId,
            IsBiographical = true,
            BiographicalEra = string.IsNullOrWhiteSpace(era) ? null : era.Trim(),
            BiographicalSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            IsCompletelyPrivate = false,
            ShowOnlineStatus = false,
            CreatedAt = DateTime.UtcNow,
            // Long expiry so the account can never authenticate even if the
            // password were somehow known. AccountController also refuses
            // login for managed accounts, but defense in depth.
            LockoutEnd = DateTimeOffset.MaxValue,
            LockoutEnabled = true
        };

        // Random password we never surface anywhere.
        var result = await _userManager.CreateAsync(user, $"Managed!{Guid.NewGuid():N}A");
        if (!result.Succeeded)
        {
            TempData["Error"] = "Could not create profile: " + string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(CreateManagedUser));
        }

        TempData["Success"] = $"Biographical profile '{user.DisplayName}' created. You can now post as them from the New Story page.";
        return RedirectToAction(nameof(ManagedUsers));
    }

    [HttpGet]
    public async Task<IActionResult> EditManagedUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id ?? "");
        if (user == null || (string.IsNullOrEmpty(user.ManagedByUserId) && !user.IsBiographical))
        {
            return NotFound();
        }
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditManagedUser(string id, string displayName, string? era, string? summary, string? profilePhotoUrl)
    {
        var user = await _userManager.FindByIdAsync(id ?? "");
        if (user == null || (string.IsNullOrEmpty(user.ManagedByUserId) && !user.IsBiographical))
        {
            return NotFound();
        }

        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? user.UserName : displayName.Trim();
        user.BiographicalEra = string.IsNullOrWhiteSpace(era) ? null : era.Trim();
        user.BiographicalSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        if (!string.IsNullOrWhiteSpace(profilePhotoUrl))
        {
            user.ProfilePhotoUrl = profilePhotoUrl.Trim();
        }
        await _userManager.UpdateAsync(user);
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(ManagedUsers));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteManagedUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id ?? "");
        if (user == null) return RedirectToAction(nameof(ManagedUsers));
        // Only allow delete on managed/biographical accounts via this path.
        if (string.IsNullOrEmpty(user.ManagedByUserId) && !user.IsBiographical)
        {
            TempData["Error"] = "That account isn't a biographical profile.";
            return RedirectToAction(nameof(ManagedUsers));
        }

        var name = user.DisplayName ?? user.UserName ?? "(profile)";
        var ok = await _deletion.DeleteUserAsync(user.Id);
        TempData[ok ? "Success" : "Error"] = ok
            ? $"Biographical profile '{name}' deleted along with all of its posts."
            : "Could not delete the profile.";
        return RedirectToAction(nameof(ManagedUsers));
    }

    // ── Site settings (runtime feature flags) ─────────────────────────────

    [HttpGet]
    public async Task<IActionResult> SiteSettings()
    {
        ViewBag.ChannelsEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.ChannelsEnabled, true);
        ViewBag.BiographicalEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.BiographicalEnabled, true);
        ViewBag.EvergreenSurfacing = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenSurfacing, true);

        ViewBag.EvergreenChannelMaxPerPage     = await _siteSettings.GetIntAsync(ISiteSettings.EvergreenChannelMaxPerPage, 3);
        ViewBag.EvergreenChannelPosition       = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenChannelPosition, "random");
        ViewBag.EvergreenChannelOrder          = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenChannelOrder, "random");
        ViewBag.EvergreenChannelAllowBackToBack = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenChannelAllowBackToBack, false);
        ViewBag.EvergreenChannelDailyOnePerSource = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenChannelDailyOnePerSource, true);

        ViewBag.EvergreenBioMaxPerPage     = await _siteSettings.GetIntAsync(ISiteSettings.EvergreenBioMaxPerPage, 2);
        ViewBag.EvergreenBioPosition       = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenBioPosition, "random");
        ViewBag.EvergreenBioOrder          = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenBioOrder, "random");
        ViewBag.EvergreenBioAllowBackToBack = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenBioAllowBackToBack, false);
        ViewBag.EvergreenBioDailyOnePerSource = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenBioDailyOnePerSource, true);

        ViewBag.BannerEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.BannerEnabled, false);
        ViewBag.BannerText = await _siteSettings.GetStringAsync(ISiteSettings.BannerText);
        ViewBag.BannerSeverity = await _siteSettings.GetStringAsync(ISiteSettings.BannerSeverity, "info");
        ViewBag.BannerLinkUrl = await _siteSettings.GetStringAsync(ISiteSettings.BannerLinkUrl);
        ViewBag.BannerLinkText = await _siteSettings.GetStringAsync(ISiteSettings.BannerLinkText);
        ViewBag.BannerVersion = await _siteSettings.GetIntAsync(ISiteSettings.BannerVersion, 0);

        ViewBag.OnboardingOverlayEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.OnboardingOverlayEnabled, false);
        ViewBag.PremiumEnforcementActive = await _siteSettings.GetBoolAsync(ISiteSettings.PremiumEnforcementActive, false);

        ViewBag.WhatsNewEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.WhatsNewEnabled, false);
        ViewBag.WhatsNewTitle = await _siteSettings.GetStringAsync(ISiteSettings.WhatsNewTitle);
        ViewBag.WhatsNewBody = await _siteSettings.GetStringAsync(ISiteSettings.WhatsNewBody);
        ViewBag.WhatsNewVersion = await _siteSettings.GetIntAsync(ISiteSettings.WhatsNewVersion, 0);

        return View();
    }

    // ── Creator's notes — admin-only scratchpad ───────────────────────
    // Lives on its own page (Admin → Creator's Notes) instead of being
    // buried in Site Settings. Two free-text lists, one item per line.
    // First read seeds the textareas with the working roadmap so the
    // admin has a starting point to edit; once saved (even empty) the
    // saved value wins.

    private static readonly string[] DefaultPremiumFeatures =
    {
        "Audio + video recording",
        "Newspaper / Book layouts",
        "Per-image photo positioning",
        "Family Groups + Family chats",
        "Family Tree",
        "View as a book",
        "World travel map",
        "Voice → story dictation",
        "AI interview mode",
        "Smart prompts from the Working Index",
        "Anniversary feed + on-this-day digest",
        "Heir / Successor account + posthumous read-only",
        "Time-locked stories",
        "Co-authoring drafts",
        "Crowdsource a memory invites",
        "Self-host encrypted export"
    };

    private static readonly string[] DefaultPremiumServices =
    {
        "Hardcover printing",
        "Per-decade booklet",
        "Memorial / remembrance book",
        "Photo restoration (human-quality)",
        "Editing / ghost-writing",
        "Human transcription",
        "Translation",
        "Custom book design",
        "Wedding / anniversary / birthday gift book",
        "Audio book",
        "Time-capsule mailing",
        "Family-tree research"
    };

    [HttpGet]
    public async Task<IActionResult> CreatorNotes()
    {
        var savedFeatures = await _siteSettings.GetStringAsync(ISiteSettings.CreatorNotesPremiumFeatures);
        var savedServices = await _siteSettings.GetStringAsync(ISiteSettings.CreatorNotesPremiumServices);
        ViewBag.CreatorNotesPremiumFeatures = savedFeatures ?? string.Join("\n", DefaultPremiumFeatures);
        ViewBag.CreatorNotesPremiumServices = savedServices ?? string.Join("\n", DefaultPremiumServices);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatorNotes(string? premiumFeatures, string? premiumServices)
    {
        await _siteSettings.SetStringAsync(ISiteSettings.CreatorNotesPremiumFeatures,
            string.IsNullOrWhiteSpace(premiumFeatures) ? null : premiumFeatures.TrimEnd());
        await _siteSettings.SetStringAsync(ISiteSettings.CreatorNotesPremiumServices,
            string.IsNullOrWhiteSpace(premiumServices) ? null : premiumServices.TrimEnd());
        TempData["Success"] = "Creator's notes saved.";
        return RedirectToAction(nameof(CreatorNotes));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SiteSettings(
        bool channelsEnabled, bool biographicalEnabled, bool evergreenSurfacing,
        int evergreenChannelMaxPerPage, string? evergreenChannelPosition, string? evergreenChannelOrder,
        bool evergreenChannelAllowBackToBack, bool evergreenChannelDailyOnePerSource,
        int evergreenBioMaxPerPage, string? evergreenBioPosition, string? evergreenBioOrder,
        bool evergreenBioAllowBackToBack, bool evergreenBioDailyOnePerSource,
        bool bannerEnabled, string? bannerText, string? bannerSeverity, string? bannerLinkUrl, string? bannerLinkText, bool bannerForceShowAll,
        bool whatsNewEnabled, string? whatsNewTitle, string? whatsNewBody, bool whatsNewForceShowAll,
        bool onboardingOverlayEnabled,
        bool premiumEnforcementActive)
    {
        await _siteSettings.SetBoolAsync(ISiteSettings.ChannelsEnabled, channelsEnabled);
        await _siteSettings.SetBoolAsync(ISiteSettings.BiographicalEnabled, biographicalEnabled);
        await _siteSettings.SetBoolAsync(ISiteSettings.EvergreenSurfacing, evergreenSurfacing);

        // Evergreen surfacing rules — clamp the numeric inputs and
        // normalize string choices so a hand-crafted form post can't
        // poison the runtime config.
        string Norm(string? raw, string[] allowed, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var lower = raw.Trim().ToLowerInvariant();
            return allowed.Contains(lower) ? lower : fallback;
        }
        await _siteSettings.SetIntAsync(ISiteSettings.EvergreenChannelMaxPerPage, Math.Clamp(evergreenChannelMaxPerPage, 0, 10));
        await _siteSettings.SetStringAsync(ISiteSettings.EvergreenChannelPosition, Norm(evergreenChannelPosition, new[] { "top", "middle", "random" }, "random"));
        await _siteSettings.SetStringAsync(ISiteSettings.EvergreenChannelOrder, Norm(evergreenChannelOrder, new[] { "random", "recent" }, "random"));
        await _siteSettings.SetBoolAsync(ISiteSettings.EvergreenChannelAllowBackToBack, evergreenChannelAllowBackToBack);
        await _siteSettings.SetBoolAsync(ISiteSettings.EvergreenChannelDailyOnePerSource, evergreenChannelDailyOnePerSource);

        await _siteSettings.SetIntAsync(ISiteSettings.EvergreenBioMaxPerPage, Math.Clamp(evergreenBioMaxPerPage, 0, 10));
        await _siteSettings.SetStringAsync(ISiteSettings.EvergreenBioPosition, Norm(evergreenBioPosition, new[] { "top", "middle", "random" }, "random"));
        await _siteSettings.SetStringAsync(ISiteSettings.EvergreenBioOrder, Norm(evergreenBioOrder, new[] { "random", "recent" }, "random"));
        await _siteSettings.SetBoolAsync(ISiteSettings.EvergreenBioAllowBackToBack, evergreenBioAllowBackToBack);
        await _siteSettings.SetBoolAsync(ISiteSettings.EvergreenBioDailyOnePerSource, evergreenBioDailyOnePerSource);

        // Site banner. We track an internal version so any edit re-shows the
        // banner to users who'd already dismissed an earlier copy. The
        // "force show to all" switch is a manual nudge — bumps the version
        // even if the text didn't change.
        var prevBannerText = await _siteSettings.GetStringAsync(ISiteSettings.BannerText);
        var prevSeverity   = await _siteSettings.GetStringAsync(ISiteSettings.BannerSeverity, "info");
        var prevLinkUrl    = await _siteSettings.GetStringAsync(ISiteSettings.BannerLinkUrl);
        var prevLinkText   = await _siteSettings.GetStringAsync(ISiteSettings.BannerLinkText);
        var bannerChanged = prevBannerText != bannerText
                         || (prevSeverity ?? "info") != (bannerSeverity ?? "info")
                         || (prevLinkUrl ?? "") != (bannerLinkUrl ?? "")
                         || (prevLinkText ?? "") != (bannerLinkText ?? "");

        await _siteSettings.SetBoolAsync(ISiteSettings.BannerEnabled, bannerEnabled);
        await _siteSettings.SetStringAsync(ISiteSettings.BannerText, string.IsNullOrWhiteSpace(bannerText) ? null : bannerText.Trim());
        await _siteSettings.SetStringAsync(ISiteSettings.BannerSeverity, string.IsNullOrWhiteSpace(bannerSeverity) ? "info" : bannerSeverity.Trim());
        await _siteSettings.SetStringAsync(ISiteSettings.BannerLinkUrl, string.IsNullOrWhiteSpace(bannerLinkUrl) ? null : bannerLinkUrl.Trim());
        await _siteSettings.SetStringAsync(ISiteSettings.BannerLinkText, string.IsNullOrWhiteSpace(bannerLinkText) ? null : bannerLinkText.Trim());
        if (bannerChanged || bannerForceShowAll)
        {
            var v = await _siteSettings.GetIntAsync(ISiteSettings.BannerVersion, 0);
            await _siteSettings.SetIntAsync(ISiteSettings.BannerVersion, v + 1);
        }

        // What's new modal — same versioning trick. Edits or "force show"
        // bump the version so users see it once.
        var prevWnTitle = await _siteSettings.GetStringAsync(ISiteSettings.WhatsNewTitle);
        var prevWnBody  = await _siteSettings.GetStringAsync(ISiteSettings.WhatsNewBody);
        var wnChanged = prevWnTitle != whatsNewTitle || prevWnBody != whatsNewBody;

        await _siteSettings.SetBoolAsync(ISiteSettings.WhatsNewEnabled, whatsNewEnabled);
        await _siteSettings.SetStringAsync(ISiteSettings.WhatsNewTitle, string.IsNullOrWhiteSpace(whatsNewTitle) ? null : whatsNewTitle.Trim());
        await _siteSettings.SetStringAsync(ISiteSettings.WhatsNewBody, string.IsNullOrWhiteSpace(whatsNewBody) ? null : whatsNewBody.Trim());
        if (wnChanged || whatsNewForceShowAll)
        {
            var v = await _siteSettings.GetIntAsync(ISiteSettings.WhatsNewVersion, 0);
            await _siteSettings.SetIntAsync(ISiteSettings.WhatsNewVersion, v + 1);
        }

        await _siteSettings.SetBoolAsync(ISiteSettings.OnboardingOverlayEnabled, onboardingOverlayEnabled);
        await _siteSettings.SetBoolAsync(ISiteSettings.PremiumEnforcementActive, premiumEnforcementActive);

        TempData["Success"] = "Site settings saved.";
        return RedirectToAction(nameof(SiteSettings));
    }

    // ── Premium catalog (read-only admin view) ────────────────────────
    // The single source of truth lives in C# (PremiumService.Catalog).
    // This view just renders it so admins can see what's tagged premium
    // and at what tier without spelunking the source.
    [HttpGet]
    public async Task<IActionResult> PremiumCatalog([FromServices] IPremiumService premium)
    {
        ViewBag.EnforcementActive = await premium.EnforcementActiveAsync();

        // Per-feature mode so the row can render its current state.
        var modes = new Dictionary<PremiumFeature, FeatureMode>();
        foreach (var f in premium.Catalog)
        {
            modes[f.Key] = await premium.GetModeAsync(f.Key);
        }
        ViewBag.FeatureModes = modes;
        return View(premium.Catalog);
    }

    // POST: /Admin/SetFeatureMode — set a single feature's availability
    // mode. Three choices: All (open to everyone), Premium (subscribers
    // + admins), Off (admins only). Independent of global enforcement.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFeatureMode(
        PremiumFeature feature,
        FeatureMode mode,
        [FromServices] IPremiumService premium)
    {
        await premium.SetModeAsync(feature, mode);
        TempData["Success"] = mode switch
        {
            FeatureMode.All     => $"{feature} is now available to ALL users.",
            FeatureMode.Premium => $"{feature} is now available to PREMIUM subscribers only (admins still see it).",
            FeatureMode.Off     => $"{feature} is now hidden from regular users (admins still see it).",
            _                   => $"{feature} mode set."
        };
        return RedirectToAction(nameof(PremiumCatalog));
    }

    // POST: /Admin/SetPremium — grant or clear a user's premium access
    // manually. Used during beta to comp testers; eventually replaced
    // by Stripe webhook flow. Pass `untilUtc=""` to clear.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPremium(string userId, string? untilUtc, string? tier)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["Error"] = "Missing user.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        if (string.IsNullOrWhiteSpace(untilUtc))
        {
            user.PremiumUntil = null;
            user.PremiumTier = null;
            await _userManager.UpdateAsync(user);
            TempData["Success"] = $"Premium cleared for {user.UserName}.";
            return RedirectToAction(nameof(Users));
        }

        if (!DateTime.TryParse(untilUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var until))
        {
            TempData["Error"] = "Invalid expiry date.";
            return RedirectToAction(nameof(Users));
        }
        if (until <= DateTime.UtcNow)
        {
            TempData["Error"] = "Expiry must be in the future.";
            return RedirectToAction(nameof(Users));
        }

        var resolvedTier = "Personal";
        if (!string.IsNullOrWhiteSpace(tier) && Enum.TryParse<MyStoryTold.Models.PremiumTier>(tier, true, out var t))
        {
            resolvedTier = t.ToString();
        }

        user.PremiumUntil = until;
        user.PremiumTier = resolvedTier;
        await _userManager.UpdateAsync(user);

        TempData["Success"] = $"Premium granted to {user.UserName} ({resolvedTier}) until {until:MMM d, yyyy}.";
        return RedirectToAction(nameof(Users));
    }

    // ── Reported content / users ──────────────────────────────────────────

    public async Task<IActionResult> Reports()
    {
        var pending = await _db.Reports
            .Where(r => r.Status == ReportStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Include(r => r.Reporter)
            .ToListAsync();
        return View(pending);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissReport(int id)
    {
        var r = await _db.Reports.FindAsync(id);
        if (r != null)
        {
            r.Status = ReportStatus.Dismissed;
            r.HandledAt = DateTime.UtcNow;
            r.HandledByUserId = _userManager.GetUserId(User);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Report dismissed.";
        }
        return RedirectToAction(nameof(Reports));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReportActioned(int id)
    {
        var r = await _db.Reports.FindAsync(id);
        if (r != null)
        {
            r.Status = ReportStatus.Actioned;
            r.HandledAt = DateTime.UtcNow;
            r.HandledByUserId = _userManager.GetUserId(User);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Report marked as actioned.";
        }
        return RedirectToAction(nameof(Reports));
    }

    // ── Account deletion requests ─────────────────────────────────────────

    public async Task<IActionResult> DeletionRequests()
    {
        var pending = await _userManager.Users
            .Where(u => u.AccountDeletionRequestedAt != null)
            .OrderBy(u => u.AccountDeletionRequestedAt)
            .ToListAsync();
        return View(pending);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessDeletionRequest(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return RedirectToAction(nameof(DeletionRequests));
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.AccountDeletionRequestedAt == null)
        {
            TempData["Error"] = "That request is no longer valid.";
            return RedirectToAction(nameof(DeletionRequests));
        }

        var name = user.DisplayName ?? user.UserName ?? user.Email ?? userId;
        var ok = await _deletion.DeleteUserAsync(userId);
        TempData["Success"] = ok ? $"Account '{name}' permanently deleted." : "Could not delete the account.";
        return RedirectToAction(nameof(DeletionRequests));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectDeletionRequest(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId ?? "");
        if (user != null)
        {
            user.AccountDeletionRequestedAt = null;
            await _userManager.UpdateAsync(user);
            TempData["Success"] = "Deletion request rejected — the user keeps their account.";
        }
        return RedirectToAction(nameof(DeletionRequests));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTip(TipType type, string text, int sortOrder, bool notifyUsers = false)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim();
            _db.Tips.Add(new Tip { Type = type, Text = trimmed, SortOrder = sortOrder, IsActive = true, CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();

            if (notifyUsers)
            {
                // Broadcast as an Announcement notification to every user.
                var allUserIds = await _db.Users.Select(u => u.Id).ToListAsync();
                var now = DateTime.UtcNow;
                var rows = allUserIds.Select(uid => new Notification
                {
                    UserId = uid,
                    Type = NotificationType.Announcement,
                    Text = trimmed,
                    LinkUrl = "/Home/Index",
                    CreatedAt = now
                });
                _db.Notifications.AddRange(rows);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Tip added and broadcast to {allUserIds.Count} users.";
            }
            else
            {
                TempData["Success"] = "Tip added.";
            }
        }
        return RedirectToAction(nameof(Tips));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTip(int id, TipType type, string text, bool isActive, int sortOrder)
    {
        var tip = await _db.Tips.FindAsync(id);
        if (tip != null)
        {
            tip.Type = type;
            tip.Text = text.Trim();
            tip.IsActive = isActive;
            tip.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Tip updated.";
        }
        return RedirectToAction(nameof(Tips));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTip(int id)
    {
        var tip = await _db.Tips.FindAsync(id);
        if (tip != null) { _db.Tips.Remove(tip); await _db.SaveChangesAsync(); TempData["Success"] = "Tip deleted."; }
        return RedirectToAction(nameof(Tips));
    }

    // ── Quill Messages (landing page typewriter) ──────────────────────────

    public async Task<IActionResult> QuillMessages()
    {
        List<QuillMessage> rows;
        try { rows = await _db.QuillMessages.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(); }
        catch { rows = new List<QuillMessage>(); }
        return View(rows);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQuillMessage(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            int next = 0;
            try { next = (await _db.QuillMessages.MaxAsync(m => (int?)m.SortOrder)) + 1 ?? 0; } catch { }
            _db.QuillMessages.Add(new QuillMessage
            {
                Text = text.Trim(),
                SortOrder = next,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Message added.";
        }
        return RedirectToAction(nameof(QuillMessages));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuillMessage(int id, string text, bool isActive)
    {
        var m = await _db.QuillMessages.FindAsync(id);
        if (m != null)
        {
            m.Text = (text ?? "").Trim();
            m.IsActive = isActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Message updated.";
        }
        return RedirectToAction(nameof(QuillMessages));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuillMessage(int id)
    {
        var m = await _db.QuillMessages.FindAsync(id);
        if (m != null) { _db.QuillMessages.Remove(m); await _db.SaveChangesAsync(); TempData["Success"] = "Message deleted."; }
        return RedirectToAction(nameof(QuillMessages));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderQuillMessages(int id, string direction)
    {
        var rows = await _db.QuillMessages.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
        var idx = rows.FindIndex(m => m.Id == id);
        if (idx < 0) return RedirectToAction(nameof(QuillMessages));

        var swap = direction == "up" ? idx - 1 : idx + 1;
        if (swap < 0 || swap >= rows.Count) return RedirectToAction(nameof(QuillMessages));

        // Renumber 0..N first to guarantee uniqueness, then swap the two values.
        for (int i = 0; i < rows.Count; i++) rows[i].SortOrder = i;
        (rows[idx].SortOrder, rows[swap].SortOrder) = (rows[swap].SortOrder, rows[idx].SortOrder);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(QuillMessages));
    }

    // ── Memory Prompts (sidebar Today's Prompt rotation) ──────────────────

    public async Task<IActionResult> MemoryPrompts()
    {
        List<MemoryPrompt> rows;
        try { rows = await _db.MemoryPrompts.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(); }
        catch { rows = new List<MemoryPrompt>(); }
        return View(rows);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMemoryPrompt(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            int next = 0;
            try { next = (await _db.MemoryPrompts.MaxAsync(m => (int?)m.SortOrder)) + 1 ?? 0; } catch { }
            _db.MemoryPrompts.Add(new MemoryPrompt
            {
                Text = text.Trim(),
                SortOrder = next,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Prompt added.";
        }
        return RedirectToAction(nameof(MemoryPrompts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMemoryPrompt(int id, string text, bool isActive)
    {
        var m = await _db.MemoryPrompts.FindAsync(id);
        if (m != null)
        {
            m.Text = (text ?? "").Trim();
            m.IsActive = isActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Prompt updated.";
        }
        return RedirectToAction(nameof(MemoryPrompts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMemoryPrompt(int id)
    {
        var m = await _db.MemoryPrompts.FindAsync(id);
        if (m != null) { _db.MemoryPrompts.Remove(m); await _db.SaveChangesAsync(); TempData["Success"] = "Prompt deleted."; }
        return RedirectToAction(nameof(MemoryPrompts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderMemoryPrompts(int id, string direction)
    {
        var rows = await _db.MemoryPrompts.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
        var idx = rows.FindIndex(m => m.Id == id);
        if (idx < 0) return RedirectToAction(nameof(MemoryPrompts));

        var swap = direction == "up" ? idx - 1 : idx + 1;
        if (swap < 0 || swap >= rows.Count) return RedirectToAction(nameof(MemoryPrompts));

        for (int i = 0; i < rows.Count; i++) rows[i].SortOrder = i;
        (rows[idx].SortOrder, rows[swap].SortOrder) = (rows[swap].SortOrder, rows[idx].SortOrder);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(MemoryPrompts));
    }

    // ── Email diagnostics ─────────────────────────────────────────────────

    public IActionResult EmailTest()
    {
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailTest(string toEmail, [FromServices] Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            ViewBag.Result = "Enter a recipient email.";
            ViewBag.Success = false;
            return View();
        }

        if (emailSender is MyStoryTold.Services.EmailSender es)
        {
            var diag = await es.SendDiagnosticAsync(
                toEmail,
                "Kronoscript email test",
                $"<p>This is a test email from Kronoscript.</p><p>If you can read this, SendGrid is wired up correctly.</p><p>Sent at {DateTime.UtcNow:u} UTC.</p>");
            ViewBag.Success = diag.Success;
            ViewBag.Result = $"Status: {diag.StatusCode}\nFrom: {diag.From}\nDetail: {diag.Message}";
        }
        else
        {
            await emailSender.SendEmailAsync(toEmail, "Kronoscript email test", "<p>Test from Kronoscript.</p>");
            ViewBag.Success = true;
            ViewBag.Result = "Sent (no diagnostics available — non-default email sender).";
        }
        ViewBag.ToEmail = toEmail;
        return View();
    }

    // ── User Feed (admin view) ────────────────────────────────────────────

    public async Task<IActionResult> UserFeed(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var posts = await _db.LifeEventPosts
            .Include(p => p.Comments)
            .Include(p => p.Likes)
            .Include(p => p.Media)
            .Where(p => p.OwnerUserId == id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        ViewBag.ProfileUser = user;
        return View(posts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUser(string userId, string? reason)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        // Prevent removing another admin
        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Error"] = "Cannot remove an admin account.";
            return RedirectToAction(nameof(Users));
        }

        var adminId = _userManager.GetUserId(User);
        var email = user.Email!;

        // Add a permanent ban on the email before deleting
        // Remove any previous bans for this user first
        var existing = await _db.UserBans.Where(b => b.UserId == userId).ToListAsync();
        _db.UserBans.RemoveRange(existing);

        _db.UserBans.Add(new UserBan
        {
            UserId = null, // will be null after deletion
            BannedEmail = email,
            BanType = BanType.Permanent,
            BannedAt = DateTime.UtcNow,
            BanExpiry = null,
            BannedByUserId = adminId,
            Reason = reason
        });

        await _db.SaveChangesAsync();

        // Delete the user (cascades to posts, comments, likes)
        await _userManager.DeleteAsync(user);

        TempData["Success"] = $"User account ({email}) has been permanently removed and banned.";
        return RedirectToAction(nameof(Users));
    }
}
