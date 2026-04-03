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

        var recentPosts = await _db.LifeEventPosts
            .Where(p => friendIds.Contains(p.OwnerUserId))
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { UserId = g.Key, LastAt = g.Max(p => p.CreatedAt) })
            .ToListAsync();

        var recentComments = await _db.Comments
            .Where(c => friendIds.Contains(c.AuthorUserId))
            .GroupBy(c => c.AuthorUserId)
            .Select(g => new { UserId = g.Key, LastAt = g.Max(c => c.CreatedAt) })
            .ToListAsync();

        // Merge: take most recent activity (post or comment) per friend
        var activityMap = recentPosts.ToDictionary(x => x.UserId, x => x.LastAt);
        foreach (var rc in recentComments)
        {
            if (!activityMap.TryGetValue(rc.UserId, out var existing) || rc.LastAt > existing)
                activityMap[rc.UserId] = rc.LastAt;
        }

        var activeFriends = friendList.Friends
            .Select(f => new ActiveFriendViewModel
            {
                User = f.User,
                LastPostedAt = activityMap.TryGetValue(f.User.Id, out var lastAt) ? lastAt : (DateTime?)null
            })
            .Where(f => f.LastPostedAt != null)
            .OrderByDescending(f => f.LastPostedAt)
            .Take(5)
            .ToList();

        List<Tip> tips;
        try
        {
            tips = await _db.Tips
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync();
        }
        catch { tips = new List<Tip>(); }

        // Fallback defaults when the DB has no tips yet
        if (!tips.Any())
        {
            tips = new List<Tip>
            {
                new() { Type = TipType.New,  Text = "You can now tag friends in your life events!", SortOrder = 0 },
                new() { Type = TipType.Tip,  Text = "Use Export My Story to save your timeline as a document.", SortOrder = 1 },
                new() { Type = TipType.Tip,  Text = "Set post visibility to control who sees each memory.", SortOrder = 2 },
                new() { Type = TipType.Info, Text = "Invite relatives to co-author your family story.", SortOrder = 3 },
            };
        }

        // Share tips with the right sidebar partial via HttpContext.Items
        HttpContext.Items["SidebarTips"] = tips;

        var vm = new DashboardViewModel
        {
            FriendsCount = friendsCount,
            AcquaintancesCount = acquaintancesCount,
            FamilyCount = familyCount,
            TaggedCount = taggedCount,
            PendingRequestsCount = pendingRequestsCount,
            ActiveFriends = activeFriends,
            Tips = tips
        };

        return View(vm);
    }
}
