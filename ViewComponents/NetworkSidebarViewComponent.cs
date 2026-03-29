using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.ViewComponents;

public class NetworkSidebarViewComponent : ViewComponent
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFriendService _friendService;
    private readonly ApplicationDbContext _db;

    public NetworkSidebarViewComponent(
        UserManager<ApplicationUser> userManager,
        IFriendService friendService,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _friendService = friendService;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = _userManager.GetUserId(HttpContext.User);
        if (userId == null) return View(new DashboardViewModel());

        var friendList = await _friendService.GetFriendListAsync(userId);

        var friendsCount = friendList.Friends.Count(f => f.Tier == FriendTier.Friend);
        var acquaintancesCount = friendList.Friends.Count(f => f.Tier == FriendTier.Acquaintance);
        var familyCount = friendList.Friends.Count(f => f.Tier == FriendTier.Family);

        var taggedCount = await _db.LifeEventPosts
            .Where(p => p.TaggedUserIds != null && p.TaggedUserIds.Contains(userId))
            .CountAsync();

        var pendingRequestsCount = await _db.FriendConnections
            .Where(c => c.AddresseeUserId == userId && c.Status == FriendConnectionStatus.Pending)
            .CountAsync();

        var friendIds = friendList.Friends.Select(f => f.User.Id).ToList();
        var recentPosterIds = await _db.LifeEventPosts
            .Where(p => friendIds.Contains(p.OwnerUserId))
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { UserId = g.Key, LastPosted = g.Max(p => p.CreatedAt) })
            .ToListAsync();

        var activeFriends = friendList.Friends
            .Select(f => new ActiveFriendViewModel
            {
                User = f.User,
                LastPostedAt = recentPosterIds.FirstOrDefault(r => r.UserId == f.User.Id)?.LastPosted
            })
            .Where(f => f.LastPostedAt != null)
            .OrderByDescending(f => f.LastPostedAt)
            .Take(5)
            .ToList();

        var vm = new DashboardViewModel
        {
            FriendsCount = friendsCount,
            AcquaintancesCount = acquaintancesCount,
            FamilyCount = familyCount,
            TaggedCount = taggedCount,
            PendingRequestsCount = pendingRequestsCount,
            ActiveFriends = activeFriends
        };

        return View(vm);
    }
}
