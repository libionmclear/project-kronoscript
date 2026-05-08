using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.ViewComponents;

public class UserBadgesViewComponent : ViewComponent
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBadgeService _badges;

    public UserBadgesViewComponent(UserManager<ApplicationUser> userManager, IBadgeService badges)
    {
        _userManager = userManager;
        _badges = badges;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = _userManager.GetUserId(HttpContext.User);
        if (string.IsNullOrEmpty(userId)) return View(new MyStoryTold.Models.ViewModels.UserBadgesViewModel());
        var ladders = await _badges.GetProgressAsync(userId);
        var founding = await _badges.GetFoundingBadgeAsync(userId);
        return View(new MyStoryTold.Models.ViewModels.UserBadgesViewModel
        {
            Ladders = ladders,
            Founding = founding
        });
    }
}
