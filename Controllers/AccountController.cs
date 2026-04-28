using System.Text;
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

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        IEmailSender emailSender)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _emailSender = emailSender;
    }

    [HttpGet]
    public IActionResult Register(string? invite = null)
    {
        ViewBag.InviteToken = invite;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? invite = null)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = model.UserName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);

            // If registered via invite, auto-send friend request from inviter
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

            return RedirectToAction("Index", "Home");
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

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
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

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
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
}
