using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.ViewComponents;

public class OnboardingProgressViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public OnboardingProgressViewComponent(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = _userManager.GetUserId(HttpContext.User);
        if (string.IsNullOrEmpty(userId)) return View(new OnboardingProgressViewModel { ShouldRender = false });

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return View(new OnboardingProgressViewModel { ShouldRender = false });

        // Check the three required items.
        var hasProfile = !string.IsNullOrWhiteSpace(user.FirstName)
                        || !string.IsNullOrWhiteSpace(user.LastName)
                        || !string.IsNullOrWhiteSpace(user.ProfilePhotoUrl);

        var hasFriend = await _db.FriendConnections
            .AnyAsync(c => (c.RequesterUserId == userId || c.AddresseeUserId == userId)
                        && c.Status == FriendConnectionStatus.Accepted);

        var hasPost = await _db.LifeEventPosts
            .AnyAsync(p => p.OwnerUserId == userId && !p.IsDraft);

        var done = (hasProfile ? 1 : 0) + (hasFriend ? 1 : 0) + (hasPost ? 1 : 0);

        var vm = new OnboardingProgressViewModel
        {
            ShouldRender = done < 3,
            HasProfile = hasProfile,
            HasFriend = hasFriend,
            HasPost = hasPost,
            Done = done,
            Total = 3
        };
        return View(vm);
    }
}
