using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly Services.IAccountDeletionService _deletion;
    private readonly Services.INotificationService _notifications;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        IEmailSender emailSender,
        Services.IAccountDeletionService deletion,
        Services.INotificationService notifications)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _emailSender = emailSender;
        _deletion = deletion;
        _notifications = notifications;
    }

    [HttpGet]
    public IActionResult Register(string? invite = null)
    {
        ViewBag.InviteToken = invite;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("anon-write")]
    public async Task<IActionResult> Register(RegisterViewModel model, string? invite = null)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = model.UserName,
            CreatedAt = DateTime.UtcNow,
            AgreedToTermsAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            // If registered via invite, queue the friend request (will activate once they verify).
            if (!string.IsNullOrEmpty(invite))
            {
                var invitation = await _db.Invitations
                    .FirstOrDefaultAsync(i => i.Token == invite && !i.Used);
                if (invitation != null)
                {
                    invitation.Used = true;
                    _db.FriendConnections.Add(new FriendConnection
                    {
                        RequesterUserId = invitation.InviterUserId,
                        AddresseeUserId = user.Id,
                        Status = FriendConnectionStatus.Pending,
                        Tier = FriendTier.Acquaintance,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
            }

            // Email a verification link. Until the user clicks it they can't sign in
            // (RequireConfirmedEmail = true).
            await SendEmailConfirmationAsync(user);
            TempData["Info"] = $"Almost there! We sent a verification link to {user.Email}. Click it to activate your account.";
            return RedirectToAction(nameof(ConfirmEmailSent));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        // Find user by email or username
        var user = await _userManager.FindByEmailAsync(model.Email)
                ?? await _userManager.FindByNameAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        // Biographical / managed accounts can't log in directly. The admin
        // posts on their behalf via the "Post as" picker. Refuse before we
        // even check the password so a mistakenly-set password is still inert.
        if (!string.IsNullOrEmpty(user.ManagedByUserId) || user.IsBiographical)
        {
            ModelState.AddModelError(string.Empty, "This is a biographical profile. It can't be logged into directly.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            try
            {
                // Check for an active ban
                var now = DateTime.UtcNow;
                var activeBan = await _db.UserBans.FirstOrDefaultAsync(b =>
                    b.UserId == user.Id &&
                    (b.BanType == BanType.Permanent ||
                     (b.BanType == BanType.Temporary && b.BanExpiry != null && b.BanExpiry > now)));

                if (activeBan != null)
                {
                    await _signInManager.SignOutAsync();
                    if (activeBan.BanType == BanType.Permanent)
                        ModelState.AddModelError(string.Empty, "This account has been permanently suspended.");
                    else
                        ModelState.AddModelError(string.Empty,
                            $"This account is suspended until {activeBan.BanExpiry!.Value:MMMM d, yyyy}. Reason: {activeBan.Reason ?? "policy violation"}");
                    return View(model);
                }

                // Voluntary 30-day pause set in Settings.
                if (user.SuspendedUntil.HasValue && user.SuspendedUntil.Value > now)
                {
                    await _signInManager.SignOutAsync();
                    ModelState.AddModelError(string.Empty,
                        $"You paused this account until {user.SuspendedUntil.Value:MMMM d, yyyy}. Login will work again automatically after that.");
                    return View(model);
                }

                // Stamp last-seen on login (the throttled middleware also covers
                // it on subsequent requests; this just ensures the very first
                // page-load timestamp is correct) and clear the progressive-
                // lockout tracker so the next first-time lockout is the short one.
                user.LastSeenAt = DateTime.UtcNow;
                if (user.RecentLockoutCount > 0)
                {
                    user.RecentLockoutCount = 0;
                }
                await _userManager.UpdateAsync(user);
            }
            catch
            {
                // Ban check or activity update failed (migration may still be pending) — allow login to proceed
            }

            // Mirror the user's saved UI language into the localization
            // cookie so the next render is in their language without
            // waiting for them to toggle it.
            if (!string.IsNullOrWhiteSpace(user.PreferredUiLanguage))
            {
                var allowed = new[] { "en", "it" };
                var lang = allowed.Contains(user.PreferredUiLanguage.ToLowerInvariant())
                    ? user.PreferredUiLanguage.ToLowerInvariant()
                    : "en";
                Response.Cookies.Append(
                    Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                    Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
                        new Microsoft.AspNetCore.Localization.RequestCulture(lang)),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = false }
                );
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        if (result.IsNotAllowed)
        {
            // Most likely cause: email not yet confirmed.
            ModelState.AddModelError(string.Empty,
                "Your email isn't verified yet. Check your inbox for the verification link, or request a new one below.");
            ViewData["ShowResendLink"] = true;
            ViewData["ResendEmail"] = user.Email;
            return View(model);
        }

        if (result.IsLockedOut)
        {
            // Identity locked the account using DefaultLockoutTimeSpan (5 min).
            // If this is a repeat lockout, escalate to 30 min and increment the
            // tracker so future lockouts in this streak stay long.
            user.RecentLockoutCount += 1;
            int minutes;
            if (user.RecentLockoutCount >= 2)
            {
                minutes = 30;
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(minutes));
            }
            else
            {
                minutes = 5; // already what Identity set; no override needed
            }
            await _userManager.UpdateAsync(user);

            ModelState.AddModelError(string.Empty,
                $"Your account is locked for {minutes} minutes after too many failed attempts. You can reset your password now to unlock it.");
            return View(model);
        }

        // Wrong password but not yet locked out — surface how many attempts are
        // left so the user can decide to reset before the account locks.
        const int maxAttempts = 5; // mirrors options.Lockout.MaxFailedAccessAttempts in Program.cs
        var failedCount = await _userManager.GetAccessFailedCountAsync(user);
        var remaining = maxAttempts - failedCount;
        if (remaining > 0 && remaining <= 3)
        {
            ModelState.AddModelError(string.Empty,
                $"Invalid login attempt. {remaining} attempt{(remaining == 1 ? "" : "s")} left before your account is locked — consider resetting your password.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // POST: /Account/SetLanguage?lang=it&returnUrl=/foo
    // Sets the standard ASP.NET Core localization cookie so the next
    // request renders in the chosen culture, and (for signed-in users)
    // mirrors the choice into ApplicationUser.PreferredUiLanguage so it
    // sticks across devices on next sign-in. Anonymous visitors get the
    // cookie only.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLanguage(string lang, string? returnUrl = null)
    {
        var allowed = new[] { "en", "it" };
        var culture = string.IsNullOrWhiteSpace(lang)
            ? "en"
            : (allowed.Contains(lang.Trim().ToLowerInvariant()) ? lang.Trim().ToLowerInvariant() : "en");

        Response.Cookies.Append(
            Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
            Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
                new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = false }
        );

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null && user.PreferredUiLanguage != culture)
            {
                user.PreferredUiLanguage = culture;
                await _userManager.UpdateAsync(user);
            }
        }

        // Bounce back to where they came from (Url.IsLocalUrl guards open
        // redirects). Default to Home if no safe target.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("anon-write")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user != null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callbackUrl = Url.Action("ResetPassword", "Account",
                new { email = user.Email, token = encodedToken }, protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(user.Email!, "Reset your Kronoscript password",
                $@"<p>Hi {user.DisplayName ?? user.UserName},</p>
                   <p>We received a request to reset your Kronoscript password.</p>
                   <p><a href='{callbackUrl}' style='background:#1e4d2e;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block;'>Reset Password</a></p>
                   <p>This link expires in 24 hours. If you didn't request this, you can safely ignore this email.</p>
                   <p style='color:#888;font-size:12px;'>— The Kronoscript Team</p>");
        }

        // Always show success to prevent email enumeration
        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            return RedirectToAction("ForgotPassword");

        var model = new ResetPasswordViewModel { Email = email, Token = token };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
            return RedirectToAction("ResetPasswordConfirmation");

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid or has expired. Please request a new one.");
            return View(model);
        }

        var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.Password);
        if (result.Succeeded)
        {
            // Unlock the account if it's currently locked, clear the progressive-
            // lockout tracker, and seed AccessFailedCount so the user gets exactly
            // 3 attempts to type the new password before re-locking (5 - 2 = 3).
            await _userManager.SetLockoutEndDateAsync(user, null);
            user.RecentLockoutCount = 0;
            user.AccessFailedCount = Math.Max(0, _userManager.Options.Lockout.MaxFailedAccessAttempts - 3);
            await _userManager.UpdateAsync(user);
            return RedirectToAction("ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ───── Voluntary 30-day suspension ──────────────────────────────────────
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SuspendVoluntary()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        user.SuspendedUntil = DateTime.UtcNow.AddDays(30);
        await _userManager.UpdateAsync(user);
        await _signInManager.SignOutAsync();

        TempData["Info"] = $"Account paused until {user.SuspendedUntil:MMMM d, yyyy}. Login will work again automatically after that.";
        return RedirectToAction("Index", "Home");
    }

    // ───── Account deletion (two-step: email a code, then verify it) ────────
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestDeletion()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (string.IsNullOrEmpty(user.Email)) return RedirectToAction("Edit", "Profile");

        // Six-digit code; we store its SHA-256 hash and never the code itself.
        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        user.DeletionCodeHash = HashCode(code);
        user.DeletionCodeExpiresAt = DateTime.UtcNow.AddMinutes(30);
        await _userManager.UpdateAsync(user);

        await _emailSender.SendEmailAsync(user.Email, "Confirm your Kronoscript account deletion",
            $@"<p>Hi {user.DisplayName ?? user.UserName},</p>
               <p>You asked to delete your Kronoscript account. To confirm, enter this code on the confirmation page:</p>
               <p style='font-size:1.6rem;font-weight:700;letter-spacing:0.2em;'>{code}</p>
               <p>The code expires in 30 minutes. If you didn't request this, ignore this email and consider changing your password.</p>
               <p style='color:#888;font-size:12px;'>— Kronoscript</p>");

        return RedirectToAction(nameof(ConfirmDeletion));
    }

    [Authorize, HttpGet]
    public IActionResult ConfirmDeletion() => View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDeletion(string code, bool confirm)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!confirm)
        {
            ModelState.AddModelError(string.Empty, "You must tick the confirmation box.");
            return View();
        }
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            ModelState.AddModelError(string.Empty, "Enter the 6-digit code from the email.");
            return View();
        }
        if (string.IsNullOrEmpty(user.DeletionCodeHash) || user.DeletionCodeExpiresAt == null
            || user.DeletionCodeExpiresAt < DateTime.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "That code has expired. Request a new one from Settings.");
            return View();
        }
        if (!string.Equals(HashCode(code.Trim()), user.DeletionCodeHash, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "That code is incorrect. Check the email or request a new one.");
            return View();
        }

        // Verified — wipe content + remove the account.
        await _signInManager.SignOutAsync();
        await _deletion.DeleteUserAsync(user.Id);

        TempData["Info"] = "Your account and all your content have been permanently deleted. We're sorry to see you go.";
        return RedirectToAction("Index", "Home");
    }

    // ───── Ask admin to delete (fallback when the email-code flow fails) ────
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestAdminDeletion()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (user.AccountDeletionRequestedAt != null)
        {
            TempData["Info"] = "Your deletion request is already pending. An admin will handle it soon.";
            return RedirectToAction("Edit", "Profile");
        }

        user.AccountDeletionRequestedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Notify every admin in-app and surface a queue link.
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        var name = user.DisplayName ?? user.UserName ?? user.Email ?? "A user";
        foreach (var admin in admins)
        {
            await _notifications.CreateAsync(
                admin.Id,
                Models.NotificationType.Announcement,
                $"{name} requested account deletion",
                "/Admin/DeletionRequests",
                user.Id);
        }

        TempData["Info"] = "Your deletion request has been sent to an admin. You'll be notified by email when it's processed. You can keep using the account in the meantime, or cancel the request from this page.";
        return RedirectToAction("Edit", "Profile");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelAdminDeletionRequest()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (user.AccountDeletionRequestedAt != null)
        {
            user.AccountDeletionRequestedAt = null;
            await _userManager.UpdateAsync(user);
            TempData["Success"] = "Deletion request cancelled.";
        }
        return RedirectToAction("Edit", "Profile");
    }

    // ───── Email verification ───────────────────────────────────────────────
    [HttpGet]
    public IActionResult ConfirmEmailSent() => View();

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return View("ConfirmEmailFailed");
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return View("ConfirmEmailFailed");

        string decoded;
        try { decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return View("ConfirmEmailFailed"); }

        var result = await _userManager.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded) return View("ConfirmEmailFailed");

        await _signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Email verified — welcome to Kronoscript!";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("anon-write")]
    public async Task<IActionResult> ResendConfirmation(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Info"] = "Enter the email you used to sign up.";
            return RedirectToAction(nameof(ConfirmEmailSent));
        }
        var user = await _userManager.FindByEmailAsync(email);
        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            await SendEmailConfirmationAsync(user);
        }
        // Always show the same message to avoid leaking which addresses exist.
        TempData["Info"] = "If that account exists and isn't verified yet, we just sent a fresh link.";
        return RedirectToAction(nameof(ConfirmEmailSent));
    }

    private async Task SendEmailConfirmationAsync(ApplicationUser user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = Url.Action("ConfirmEmail", "Account",
            new { userId = user.Id, token = encoded }, protocol: Request.Scheme);

        await _emailSender.SendEmailAsync(user.Email!, "Verify your Kronoscript account",
            $@"<p>Hi {user.DisplayName ?? user.UserName},</p>
               <p>Thanks for signing up for Kronoscript. Please verify your email so you can sign in:</p>
               <p><a href='{url}' style='background:#1e4d2e;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block;'>Verify my email</a></p>
               <p>If you didn't create this account, you can safely ignore this email — no account is created until verification.</p>
               <p style='color:#888;font-size:12px;'>— Kronoscript</p>");
    }

    private static string HashCode(string code)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(code);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

}
