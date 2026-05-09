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

        // Last activity per friend in a single round-trip: UNION posts and
        // comments, group by user, take max CreatedAt server-side.
        var postActivity = _db.LifeEventPosts
            .Where(p => friendIds.Contains(p.OwnerUserId))
            .Select(p => new { UserId = p.OwnerUserId, At = p.CreatedAt });
        var commentActivity = _db.Comments
            .Where(c => friendIds.Contains(c.AuthorUserId))
            .Select(c => new { UserId = c.AuthorUserId, At = c.CreatedAt });
        var lastActivity = await postActivity.Concat(commentActivity)
            .GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, LastAt = g.Max(x => x.At) })
            .ToListAsync();

        var activityMap = lastActivity.ToDictionary(x => x.UserId, x => x.LastAt);
        // Folding LastSeenAt in (login / any authenticated page view) — already
        // loaded with the friend list, no extra DB cost.
        foreach (var f in friendList.Friends)
        {
            if (f.User.LastSeenAt is DateTime seenAt)
            {
                if (!activityMap.TryGetValue(f.User.Id, out var existing) || seenAt > existing)
                    activityMap[f.User.Id] = seenAt;
            }
        }

        // Build active-friends list, then merge in current online status for sorting/labels
        var activeFriends = friendList.Friends
            .Select(f => new ActiveFriendViewModel
            {
                User = f.User,
                LastPostedAt = activityMap.TryGetValue(f.User.Id, out var lastAt) ? lastAt : (DateTime?)null,
                IsOnline = MyStoryTold.Hubs.PresenceHub.IsOnline(f.User.Id) && f.User.ShowOnlineStatus
            })
            .Where(f => f.IsOnline || f.LastPostedAt != null)
            .OrderByDescending(f => f.IsOnline)
            .ThenByDescending(f => f.LastPostedAt)
            .Take(8)
            .ToList();

        // "New connections this week" badges per tier
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        int newAcq = 0, newFr = 0, newFam = 0;
        try
        {
            // SQL-side group + count by tier — no need to hydrate every row.
            var tierCounts = await _db.FriendConnections
                .Where(c => c.Status == FriendConnectionStatus.Accepted
                            && (c.RequesterUserId == userId || c.AddresseeUserId == userId)
                            && c.CreatedAt >= weekAgo)
                .GroupBy(c => c.Tier)
                .Select(g => new { Tier = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var t in tierCounts)
            {
                if (t.Tier == FriendTier.Acquaintance) newAcq = t.Count;
                else if (t.Tier == FriendTier.Friend)  newFr  = t.Count;
                else if (t.Tier == FriendTier.Family)  newFam = t.Count;
            }
        }
        catch { /* ignore — table or column may not yet have CreatedAt */ }

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

        // Pick one tip to rotate through on the sidebar — start at top, advance per page load via Session
        try
        {
            var orderedTips = tips
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToList();
            if (orderedTips.Any())
            {
                int idx = HttpContext.Session.GetInt32("RailTipRotation") ?? 0;
                idx = idx % orderedTips.Count;
                HttpContext.Items["SidebarTipRotated"] = orderedTips[idx];
                HttpContext.Session.SetInt32("RailTipRotation", (idx + 1) % orderedTips.Count);
            }
        }
        catch { /* session might not be ready yet */ }

        // Build a unified rotation list: today's prompt + active tips
        try
        {
            var rotation = new List<object>();

            // Today's memory prompt (same for everyone on a given day)
            var prompts = await _db.MemoryPrompts
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                .Select(p => p.Text)
                .ToListAsync();
            if (prompts.Any())
            {
                var idx = DateTime.UtcNow.DayOfYear % prompts.Count;
                rotation.Add(new { kind = "prompt", label = "PROMPT", text = prompts[idx] });
                HttpContext.Items["SidebarPromptText"] = prompts[idx];
            }

            // All active tips, in display order
            foreach (var t in tips.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Id))
            {
                var label = t.Type switch
                {
                    Models.TipType.New     => "NEW",
                    Models.TipType.Info    => "INFO",
                    Models.TipType.Warning => "WARNING",
                    _                      => "TIP"
                };
                rotation.Add(new { kind = t.Type.ToString().ToLower(), label, text = t.Text });
            }

            HttpContext.Items["SidebarRotation"] = rotation;
        }
        catch { /* ignore */ }

        // On This Day — your own past posts dated today
        try
        {
            var today = DateTime.UtcNow;
            var otd = await _db.LifeEventPosts
                .Where(p => p.OwnerUserId == userId
                            && !p.IsDraft
                            && p.ChannelId == null
                            && p.EventMonth == today.Month
                            && p.EventDay == today.Day
                            && p.EventYear < today.Year)
                .OrderByDescending(p => p.EventYear)
                .Take(2)
                .ToListAsync();
            HttpContext.Items["SidebarOnThisDay"] = otd;
        }
        catch { /* ignore */ }

        var vm = new DashboardViewModel
        {
            FriendsCount = friendsCount,
            AcquaintancesCount = acquaintancesCount,
            FamilyCount = familyCount,
            TaggedCount = taggedCount,
            PendingRequestsCount = pendingRequestsCount,
            ActiveFriends = activeFriends,
            Tips = tips,
            NewAcquaintancesThisWeek = newAcq,
            NewFriendsThisWeek = newFr,
            NewFamilyThisWeek = newFam
        };

        return View(vm);
    }
}
