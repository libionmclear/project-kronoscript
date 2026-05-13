using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Helpers;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// Customer-facing Premium benefits / upgrade page. Reads the same
/// catalog the admin Premium page uses, but groups features by tier
/// for the visitor's "what do I get?" question. The Subscribe action
/// is a stub for now — checkout integration comes later; this page is
/// the marketing surface that pulls people toward it.
/// </summary>
[Authorize]
public class PremiumController : Controller
{
    private readonly IPremiumService _premium;
    private readonly UserManager<ApplicationUser> _userManager;

    public PremiumController(IPremiumService premium, UserManager<ApplicationUser> userManager)
    {
        _premium = premium;
        _userManager = userManager;
    }

    // GET: /Premium
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        ViewBag.IsPremium      = user.HasPremium();
        ViewBag.CurrentTier    = user?.PremiumTier;
        ViewBag.PremiumUntil   = user?.PremiumUntil;
        ViewBag.Catalog        = _premium.Catalog;
        return View();
    }
}
