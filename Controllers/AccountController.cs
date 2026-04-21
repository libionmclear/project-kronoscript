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
            ModelState.AddModelError(string.Empty, "Your account has been locked after too many failed attempts. Please try again in 10 minutes or reset your password.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
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
            return RedirectToAction("ResetPasswordConfirmation");

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
