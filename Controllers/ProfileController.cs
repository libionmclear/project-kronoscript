using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;

    public ProfileController(UserManager<ApplicationUser> userManager, IWebHostEnvironment env)
    {
        _userManager = userManager;
        _env = env;
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
        ViewBag.IsOwner = currentUserId == id;
        return View(user);
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
            Gender = user.Gender,
            BirthPlace = user.BirthPlace,
            CurrentLocation = user.CurrentLocation,
            ExistingPhotoUrl = user.ProfilePhotoUrl,
            ExistingCardBackgroundUrl = user.ProfileCardBackgroundUrl,
            ShowOnlineStatus = user.ShowOnlineStatus,
            Nationalities = user.Nationalities
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
        user.Gender = model.Gender;
        user.BirthPlace = model.BirthPlace;
        user.CurrentLocation = model.CurrentLocation;
        user.ShowOnlineStatus = model.ShowOnlineStatus;
        user.Nationalities = model.Nationalities;

        // Handle photo upload
        if (model.ProfilePhoto != null && model.ProfilePhoto.Length > 0)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{user.Id}{Path.GetExtension(model.ProfilePhoto.FileName)}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.ProfilePhoto.CopyToAsync(stream);
            }

            user.ProfilePhotoUrl = $"/uploads/profiles/{fileName}";
        }

        // Handle card background upload
        if (model.ProfileCardBackground != null && model.ProfileCardBackground.Length > 0)
        {
            var bgDir = Path.Combine(_env.WebRootPath, "uploads", "profile-bg");
            Directory.CreateDirectory(bgDir);

            var bgName = $"{user.Id}{Path.GetExtension(model.ProfileCardBackground.FileName)}";
            var bgPath = Path.Combine(bgDir, bgName);

            using (var stream = new FileStream(bgPath, FileMode.Create))
            {
                await model.ProfileCardBackground.CopyToAsync(stream);
            }

            user.ProfileCardBackgroundUrl = $"/uploads/profile-bg/{bgName}";
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
}
