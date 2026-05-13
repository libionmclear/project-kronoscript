using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// Marketing pages for the ad-hoc Premium Services — hardcover prints,
/// editing, audio books, family-tree research, etc. List + per-service
/// detail. Distinct from the subscription Premium page (those are
/// software features that come with the recurring plan).
/// </summary>
[Authorize]
public class PremiumServicesController : Controller
{
    private readonly IPremiumServiceCatalog _catalog;

    public PremiumServicesController(IPremiumServiceCatalog catalog)
    {
        _catalog = catalog;
    }

    // GET: /PremiumServices
    public IActionResult Index()
    {
        ViewBag.Services = _catalog.All;
        return View();
    }

    // GET: /PremiumServices/Details/hardcover-printing
    public IActionResult Details(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
        var svc = _catalog.Get(id);
        if (svc == null) return NotFound();
        return View(svc);
    }
}
