using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
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

        // Find user by email
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
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

            // Update last activity
            user.LastActivityAt = now;
            await _userManager.UpdateAsync(user);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked out. Please try again later.");
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
            var callbackUrl = Url.Action("ResetPassword", "Account",
                new { email = user.Email, token }, protocol: Request.Scheme);
            // In production, send email. For MVP, show token link.
            TempData["ResetLink"] = callbackUrl;
        }

        // Always show success to prevent email enumeration
        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ResetPassword(string email, string token)
    {
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

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
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
