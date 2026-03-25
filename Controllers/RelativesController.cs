using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class RelativesController : Controller
{
    private readonly IRelativeService _relativeService;
    private readonly IFriendService _friendService;
    private readonly UserManager<ApplicationUser> _userManager;

    public RelativesController(
        IRelativeService relativeService,
        IFriendService friendService,
        UserManager<ApplicationUser> userManager)
    {
        _relativeService = relativeService;
        _friendService = friendService;
        _userManager = userManager;
    }

    // GET: /Relatives
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var vm = await _relativeService.GetRelativeListAsync(userId);
        return View(vm);
    }

    // POST: /Relatives/SendRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest(string userBId, RelationshipType relationshipType)
    {
        var userId = _userManager.GetUserId(User)!;
        try
        {
            await _relativeService.SendRequestAsync(userId, userBId, relationshipType);
            TempData["Success"] = "Relative request sent!";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("Index");
    }

    // POST: /Relatives/Accept/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _relativeService.AcceptRequestAsync(id, userId);
        TempData["Success"] = "Relative connection accepted!";
        return RedirectToAction("Index");
    }

    // POST: /Relatives/Decline/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _relativeService.DeclineRequestAsync(id, userId);
        return RedirectToAction("Index");
    }

    // POST: /Relatives/Remove/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _relativeService.RemoveAsync(id, userId);
        return RedirectToAction("Index");
    }

    // GET: /Relatives/SearchUsers?q=...
    [HttpGet]
    public async Task<IActionResult> SearchUsers(string q)
    {
        var userId = _userManager.GetUserId(User)!;
        var results = await _friendService.SearchUsersAsync(q ?? "", userId);
        return Json(results);
    }
}
