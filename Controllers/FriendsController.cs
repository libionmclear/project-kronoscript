using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class FriendsController : Controller
{
    private readonly IFriendService _friendService;
    private readonly UserManager<ApplicationUser> _userManager;

    public FriendsController(IFriendService friendService, UserManager<ApplicationUser> userManager)
    {
        _friendService = friendService;
        _userManager = userManager;
    }

    // GET: /Friends
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var vm = await _friendService.GetFriendListAsync(userId);
        return View(vm);
    }

    // GET: /Friends/Search?q=...
    [HttpGet]
    public async Task<IActionResult> Search(string q)
    {
        var userId = _userManager.GetUserId(User)!;
        var results = await _friendService.SearchUsersAsync(q ?? "", userId);
        return Json(results);
    }

    // POST: /Friends/SendRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest(string addresseeId)
    {
        var userId = _userManager.GetUserId(User)!;
        try
        {
            await _friendService.SendRequestAsync(userId, addresseeId);
            TempData["Success"] = "Friend request sent!";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("Index");
    }

    // POST: /Friends/Accept/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _friendService.AcceptRequestAsync(id, userId);
        TempData["Success"] = "Friend request accepted!";
        return RedirectToAction("Index");
    }

    // POST: /Friends/Decline/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _friendService.DeclineRequestAsync(id, userId);
        return RedirectToAction("Index");
    }

    // POST: /Friends/Remove/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        await _friendService.RemoveAsync(id, userId);
        return RedirectToAction("Index");
    }

    // POST: /Friends/SetTier
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTier(int connectionId, FriendTier tier)
    {
        var userId = _userManager.GetUserId(User)!;
        await _friendService.SetTierAsync(connectionId, userId, tier);
        TempData["Success"] = "Permission tier updated!";
        return RedirectToAction("Index");
    }
}
