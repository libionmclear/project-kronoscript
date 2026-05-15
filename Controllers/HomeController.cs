using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFriendService _friendService;
    private readonly IPostService _postService;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ISiteSettings _siteSettings;
    private readonly IAnalyticsService _analytics;

    public HomeController(
        ILogger<HomeController> logger,
        UserManager<ApplicationUser> userManager,
        IFriendService friendService,
        IPostService postService,
        ApplicationDbContext db,
        IEmailSender emailSender,
        ISiteSettings siteSettings,
        IAnalyticsService analytics)
    {
        _logger = logger;
        _userManager = userManager;
        _friendService = friendService;
        _postService = postService;
        _db = db;
        _emailSender = emailSender;
        _siteSettings = siteSettings;
        _analytics = analytics;
    }

    public async Task<IActionResult> Index(string? prompt = null, string? sort = null)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
            ViewBag.PromptHint = prompt.Trim();

        if (!User.Identity!.IsAuthenticated)
        {
            try
            {
                ViewBag.QuillMessages = await _db.QuillMessages
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .ThenBy(m => m.Id)
                    .Select(m => m.Text)
                    .ToListAsync();
            }
            catch
            {
                ViewBag.QuillMessages = new List<string>();
            }
            return View();
        }

        var userId = _userManager.GetUserId(User)!;
        var friendList = await _friendService.GetFriendListAsync(userId);

        // Tier counts
        var friendsCount = friendList.Friends.Count(f => f.Tier == FriendTier.Friend);
        var acquaintancesCount = friendList.Friends.Count(f => f.Tier == FriendTier.Acquaintance);
        var familyCount = friendList.Friends.Count(f => f.Tier == FriendTier.Family);

        // Tagged count
        var taggedCount = await _db.LifeEventPosts
            .Where(p => p.TaggedUserIds != null && p.TaggedUserIds.Contains(userId))
            .CountAsync();

        // Active friends (posted in last 30 days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var friendIds = friendList.Friends.Select(f => f.User.Id).ToList();
        var recentPosterIds = await _db.LifeEventPosts
            .Where(p => friendIds.Contains(p.OwnerUserId) && p.CreatedAt >= thirtyDaysAgo)
            .GroupBy(p => p.OwnerUserId)
            .Select(g => new { UserId = g.Key, LastPosted = g.Max(p => p.CreatedAt) })
            .ToListAsync();

        var activeFriends = friendList.Friends
            .Select(f =>
            {
                var lastPost = recentPosterIds.FirstOrDefault(r => r.UserId == f.User.Id)?.LastPosted;
                // Prefer the most recent of (last post within 30d, last seen).
                DateTime? mostRecent = lastPost;
                if (f.User.LastSeenAt.HasValue && (!mostRecent.HasValue || f.User.LastSeenAt.Value > mostRecent.Value))
                    mostRecent = f.User.LastSeenAt;
                return new ActiveFriendViewModel { User = f.User, LastPostedAt = mostRecent };
            })
            .Where(f => f.LastPostedAt != null)
            .OrderByDescending(f => f.LastPostedAt)
            .Take(8)
            .ToList();

        // Recent feed: the user's own posts + friends posts mixed. Channel
        // posts they wrote DO appear here — the home feed is the social /
        // discovery surface where channel content is meant to live for
        // everyone, including the writer. (Personal contexts like the
        // timeline and profile stats still exclude channel posts.)
        var nowUtc = DateTime.UtcNow;
        var ownPosts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId && !p.IsDraft)
            .Where(p => p.MutedUntil == null || p.MutedUntil <= nowUtc)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .Include(p => p.Channel)
            .OrderByDescending(p => p.RepublishedAt ?? p.CreatedAt)
            .Take(10)
            .ToListAsync();

        var friendPosts = await _postService.GetFeedPostsAsync(userId);

        var currentUser = await _userManager.FindByIdAsync(userId);

        // Pending people-profile claims: profiles whose ContactEmail
        // matches this user, that aren't already claimed, and where the
        // user hasn't already clicked "Not me". Empty list for users
        // who don't have a registered email (legacy edge case).
        ViewBag.PendingClaims = new List<PersonProfile>();
        if (!string.IsNullOrEmpty(currentUser?.Email))
        {
            var emailLower = currentUser.Email.Trim().ToLowerInvariant();
            ViewBag.PendingClaims = await _db.PersonProfiles
                .Include(p => p.Creator)
                .Where(p => p.ContactEmail != null
                            && p.ContactEmail.ToLower() == emailLower
                            && p.LinkedUserId == null
                            && p.ClaimDeclinedAt == null
                            && p.CreatorUserId != userId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync();
        }

        // Site-wide admin toggles cut at the source — overriding per-user
        // preferences. Per-user toggles still apply on top for users who
        // want to hide channels/bio while the feature is generally on.
        var channelsEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.ChannelsEnabled, true);
        var biographicalEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.BiographicalEnabled, true);

        // Expose feed filter state to the view so it can paint the quick
        // toggle pills with the right on/off look. We surface the
        // *visibility* (i.e., not-hidden) in plain language so the UI's
        // ON/OFF reads naturally.
        ViewBag.SiteChannelsEnabled = channelsEnabled;
        ViewBag.SiteBiographicalEnabled = biographicalEnabled;
        ViewBag.UserShowsChannels = !(currentUser?.HideChannelsInFeed ?? false);
        ViewBag.UserShowsBiographical = !(currentUser?.HideBiographicalInFeed ?? false);

        var filteredFriendPosts = friendPosts.AsEnumerable();
        if (!channelsEnabled || currentUser?.HideChannelsInFeed == true)
            filteredFriendPosts = filteredFriendPosts.Where(p => p.ChannelId == null);
        if (!biographicalEnabled || currentUser?.HideBiographicalInFeed == true)
            filteredFriendPosts = filteredFriendPosts.Where(p => p.Owner == null || !p.Owner.IsBiographical);

        // Per-item mute lists. Honored only when the corresponding master
        // switch is OFF (when "hide all" is on, individual mutes are moot
        // because the whole category is gone already).
        var mutedChannelSet = new HashSet<int>();
        if (channelsEnabled && currentUser?.HideChannelsInFeed != true && !string.IsNullOrEmpty(currentUser?.MutedChannelIds))
        {
            foreach (var p in currentUser!.MutedChannelIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(p.Trim(), out var n)) mutedChannelSet.Add(n);
        }
        var mutedBioSet = new HashSet<string>(StringComparer.Ordinal);
        if (biographicalEnabled && currentUser?.HideBiographicalInFeed != true && !string.IsNullOrEmpty(currentUser?.MutedBiographicalUserIds))
        {
            foreach (var p in currentUser!.MutedBiographicalUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                mutedBioSet.Add(p.Trim());
        }
        if (mutedChannelSet.Count > 0)
            filteredFriendPosts = filteredFriendPosts.Where(p => p.ChannelId == null || !mutedChannelSet.Contains(p.ChannelId.Value));
        if (mutedBioSet.Count > 0)
            filteredFriendPosts = filteredFriendPosts.Where(p => p.Owner == null || !p.Owner.IsBiographical || !mutedBioSet.Contains(p.OwnerUserId));

        // Sort: "popular" ranks by likes + comments * 2 within the past
        // 60 days; default ("latest") is reverse-chronological.
        var sortMode = (sort ?? "").Trim().ToLowerInvariant();
        if (sortMode != "popular") sortMode = "latest";
        ViewBag.FeedSort = sortMode;

        IEnumerable<FeedPostViewModel> orderedFeed;
        var combined = ownPosts
            .Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId),
                CurrentUserReaction = p.Likes.FirstOrDefault(l => l.UserId == userId)?.ReactionType
            })
            .Concat(filteredFriendPosts.Take(60).Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId),
                CurrentUserReaction = p.Likes.FirstOrDefault(l => l.UserId == userId)?.ReactionType
            }));

        if (sortMode == "popular")
        {
            var cutoff = DateTime.UtcNow.AddDays(-60);
            orderedFeed = combined
                .Where(p => (p.Post.RepublishedAt ?? p.Post.CreatedAt) >= cutoff)
                .OrderByDescending(p => p.LikeCount + (p.Post.Comments?.Count ?? 0) * 2)
                .ThenByDescending(p => p.Post.RepublishedAt ?? p.Post.CreatedAt);
        }
        else
        {
            // Sort by Coalesce(RepublishedAt, CreatedAt) so an admin
            // re-pushing an old story to the top actually shows up there.
            orderedFeed = combined.OrderByDescending(p => p.Post.RepublishedAt ?? p.Post.CreatedAt);
        }
        var allPosts = orderedFeed.Take(30).ToList();

        // Honor the admin's per-kind caps as TOTAL caps for the page,
        // applied to the chronological feed first so chMax=1 actually
        // means at most 1 channel post visible — even if the natural
        // chronological flow brought in two. The evergreen sprinkle
        // below then sees existing = cap and adds zero. Without this
        // step, the cap only governed the sprinkle, and a cap of 1 with
        // 2 channel posts already in the chronological feed still
        // rendered 2.
        var chTotalCap  = await _siteSettings.GetIntAsync(ISiteSettings.EvergreenChannelMaxPerPage, 3);
        var bioTotalCap = await _siteSettings.GetIntAsync(ISiteSettings.EvergreenBioMaxPerPage, 2);

        if (chTotalCap >= 0)
        {
            int kept = 0;
            allPosts = allPosts.Where(p =>
            {
                if (p.Post.ChannelId == null) return true;
                if (kept < chTotalCap) { kept++; return true; }
                return false;
            }).ToList();
        }
        if (bioTotalCap >= 0)
        {
            int kept = 0;
            allPosts = allPosts.Where(p =>
            {
                var isBio = p.Post.ChannelId == null && p.Post.Owner != null && p.Post.Owner.IsBiographical;
                if (!isBio) return true;
                if (kept < bioTotalCap) { kept++; return true; }
                return false;
            }).ToList();
        }

        // Evergreen: sprinkle 2-3 random older channel/bio posts into the
        // feed at random positions when sorting by Latest. Channel + bio
        // content is intentionally long-lived; this keeps it discoverable
        // beyond its publication day. Skipped on the Popular tab (which
        // already has an engagement-driven re-ordering).
        var evergreenEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenSurfacing, true);
        if (sortMode == "latest" && evergreenEnabled && (channelsEnabled || biographicalEnabled))
        {
            // Read each category's surfacing rules once.
            var chMax       = await _siteSettings.GetIntAsync(ISiteSettings.EvergreenChannelMaxPerPage, 3);
            var chPosition  = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenChannelPosition, "random") ?? "random";
            var chOrder     = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenChannelOrder, "random") ?? "random";
            var chBackToBack= await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenChannelAllowBackToBack, false);
            var chDaily     = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenChannelDailyOnePerSource, true);

            var bioMax      = await _siteSettings.GetIntAsync(ISiteSettings.EvergreenBioMaxPerPage, 2);
            var bioPosition = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenBioPosition, "random") ?? "random";
            var bioOrder    = await _siteSettings.GetStringAsync(ISiteSettings.EvergreenBioOrder, "random") ?? "random";
            var bioBackToBack = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenBioAllowBackToBack, false);
            var bioDaily    = await _siteSettings.GetBoolAsync(ISiteSettings.EvergreenBioDailyOnePerSource, true);

            var existingIds = allPosts.Select(p => p.Post.Id).ToHashSet();
            var twoWeeksAgo = DateTime.UtcNow.AddDays(-14);

            // The admin caps are *total page-load* caps for each kind of
            // post, not "how many extra evergreen picks" — that's the
            // intuitive reading of the setting. Count what's already there
            // from the chronological feed and only sprinkle the difference.
            // Setting it to 1 → at most 1 channel post on the page, period.
            var existingChannelCount = allPosts.Count(p => p.Post.ChannelId != null);
            var existingBioCount = allPosts.Count(p =>
                p.Post.ChannelId == null
                && p.Post.Owner != null
                && p.Post.Owner.IsBiographical);

            // Newish users (< 30 days) keep getting an unseen-first
            // preference inside the pool so they meet the back catalogue,
            // but the admin's cap remains a hard ceiling.
            var isNewishUser = currentUser != null
                && (DateTime.UtcNow - currentUser.CreatedAt).TotalDays < 30;

            var chCap  = Math.Max(0, Math.Max(0, chMax)  - existingChannelCount);
            var bioCap = Math.Max(0, Math.Max(0, bioMax) - existingBioCount);

            // Single DB hit covers both pools; we split client-side. Keeps
            // the existing-ids exclusion accurate and lets us share the
            // mute filters without re-querying.
            var evergreenPool = (chCap == 0 && bioCap == 0)
                ? new List<MyStoryTold.Models.LifeEventPost>()
                : await _db.LifeEventPosts
                    .Where(p => !p.IsDraft
                                && (p.MutedUntil == null || p.MutedUntil <= nowUtc)
                                && (p.RepublishedAt ?? p.CreatedAt) < twoWeeksAgo
                                && !existingIds.Contains(p.Id)
                                && (
                                    (channelsEnabled && chCap > 0 && p.ChannelId != null) ||
                                    (biographicalEnabled && bioCap > 0 && p.Owner != null && p.Owner.IsBiographical)
                                ))
                    .Include(p => p.Owner)
                    .Include(p => p.Media)
                    .Include(p => p.Comments)
                    .Include(p => p.Likes).ThenInclude(l => l.User)
                    .Include(p => p.Channel)
                    .OrderByDescending(p => p.RepublishedAt ?? p.CreatedAt)
                    .Take(80)
                    .ToListAsync();

            // Apply per-user toggles + per-item mutes to the pool.
            if (currentUser?.HideChannelsInFeed == true)
                evergreenPool = evergreenPool.Where(p => p.ChannelId == null).ToList();
            if (currentUser?.HideBiographicalInFeed == true)
                evergreenPool = evergreenPool.Where(p => p.Owner == null || !p.Owner.IsBiographical).ToList();
            if (mutedChannelSet.Count > 0)
                evergreenPool = evergreenPool.Where(p => p.ChannelId == null || !mutedChannelSet.Contains(p.ChannelId.Value)).ToList();
            if (mutedBioSet.Count > 0)
                evergreenPool = evergreenPool.Where(p => p.Owner == null || !p.Owner.IsBiographical || !mutedBioSet.Contains(p.OwnerUserId)).ToList();

            var seed = userId.GetHashCode() ^ DateTime.UtcNow.DayOfYear;
            var rng = new Random(seed);

            // Pick helper — applies "order" (recent vs random), "daily one
            // per source" (de-dup by ChannelId / OwnerUserId on the same
            // user-day seed), and "newish prefers unseen". Returns up to
            // `cap` candidates.
            List<MyStoryTold.Models.LifeEventPost> Pick(
                List<MyStoryTold.Models.LifeEventPost> pool,
                int cap, string order, bool dailyOnePerSource,
                Func<MyStoryTold.Models.LifeEventPost, string> sourceKey)
            {
                if (cap <= 0 || pool.Count == 0) return new List<MyStoryTold.Models.LifeEventPost>();

                IEnumerable<MyStoryTold.Models.LifeEventPost> ordered;
                if (order == "recent")
                {
                    ordered = pool.OrderByDescending(p => p.RepublishedAt ?? p.CreatedAt);
                }
                else
                {
                    // Deterministic shuffle — same user, same day, same picks.
                    ordered = pool.OrderBy(p => rng.Next());
                }

                // Newish users: prefer posts they haven't engaged with at
                // all yet. Falls through to engaged ones once the unseen
                // bucket is empty.
                if (isNewishUser)
                {
                    ordered = ordered
                        .OrderBy(p => p.Likes.Any(l => l.UserId == userId) || p.Comments.Any(c => c.AuthorUserId == userId) ? 1 : 0);
                }

                var picks = new List<MyStoryTold.Models.LifeEventPost>();
                var seenSources = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in ordered)
                {
                    if (picks.Count >= cap) break;
                    if (dailyOnePerSource)
                    {
                        var key = sourceKey(p);
                        if (!string.IsNullOrEmpty(key) && !seenSources.Add(key)) continue;
                    }
                    picks.Add(p);
                }
                return picks;
            }

            var channelPool = evergreenPool.Where(p => p.ChannelId != null).ToList();
            var bioPool     = evergreenPool.Where(p => p.ChannelId == null && p.Owner != null && p.Owner.IsBiographical).ToList();

            var channelPicks = Pick(channelPool, chCap, chOrder, chDaily,  p => p.ChannelId?.ToString() ?? "");
            var bioPicks     = Pick(bioPool,     bioCap, bioOrder, bioDaily, p => p.OwnerUserId);

            // Insert helper — places picks into the feed honoring the
            // configured "position" (top / middle / random) and skipping
            // back-to-back placements when AllowBackToBack is off. Marks
            // each insert with FromEvergreen=true so the view can show a
            // subtle "from the archive" tag if it wants.
            void InsertPicks(List<MyStoryTold.Models.LifeEventPost> picks, string position, bool allowBackToBack, string adjacencyTag)
            {
                if (picks.Count == 0) return;
                foreach (var p in picks)
                {
                    int target;
                    var max = Math.Max(2, allPosts.Count);
                    switch (position)
                    {
                        case "top":
                            // Pin under any of our own posts (which lead the
                            // feed). 0–1 means slot 1 or 2.
                            target = Math.Min(allPosts.Count, rng.Next(0, 2) + 1);
                            break;
                        case "middle":
                            // Insert around the visible-fold area: 5–10.
                            target = Math.Min(max, rng.Next(5, 11));
                            break;
                        default: // "random"
                            target = rng.Next(2, Math.Min(max, 18));
                            break;
                    }
                    target = Math.Clamp(target, 0, allPosts.Count);

                    if (!allowBackToBack)
                    {
                        // Walk away from the target if either neighbor is
                        // already an evergreen of the same kind. Bounded
                        // so we don't loop forever in a packed feed.
                        for (int tries = 0; tries < 6; tries++)
                        {
                            bool prevAdjacent = target > 0
                                && allPosts[target - 1].EvergreenTag == adjacencyTag;
                            bool nextAdjacent = target < allPosts.Count
                                && allPosts[target].EvergreenTag == adjacencyTag;
                            if (!prevAdjacent && !nextAdjacent) break;
                            target = Math.Min(target + 2, allPosts.Count);
                        }
                    }

                    allPosts.Insert(target, new FeedPostViewModel
                    {
                        Post = p,
                        LikeCount = p.Likes.Count,
                        CurrentUserLiked = p.Likes.Any(l => l.UserId == userId),
                        CurrentUserReaction = p.Likes.FirstOrDefault(l => l.UserId == userId)?.ReactionType,
                        FromEvergreen = true,
                        EvergreenTag = adjacencyTag
                    });
                }
            }

            // Bio picks first → channels second so a busy day with both
            // categories still leaves the channel post nearer the top
            // (admins explicitly tune channel placement).
            InsertPicks(bioPicks,     bioPosition, bioBackToBack, "bio");
            InsertPicks(channelPicks, chPosition,  chBackToBack,  "channel");
        }

        var ownPostCount = await _db.LifeEventPosts.CountAsync(p => p.OwnerUserId == userId && p.ChannelId == null);
        ViewBag.GreetingName = !string.IsNullOrWhiteSpace(currentUser?.FirstName)
            ? currentUser!.FirstName
            : (currentUser?.UserName ?? User.Identity!.Name);

        // On This Day: user's own past posts matching today's month/day
        var today = DateTime.UtcNow;
        var onThisDay = new List<LifeEventPost>();
        if (ownPostCount > 0)
        {
            onThisDay = await _db.LifeEventPosts
                .Where(p => p.OwnerUserId == userId
                            && !p.IsDraft
                            && p.ChannelId == null
                            && p.EventMonth == today.Month
                            && p.EventDay == today.Day
                            && p.EventYear < today.Year)
                .Include(p => p.Owner)
                .OrderByDescending(p => p.EventYear)
                .Take(3)
                .ToListAsync();
        }

        var vm = new DashboardViewModel
        {
            RecentPosts = allPosts,
            FriendsCount = friendsCount,
            AcquaintancesCount = acquaintancesCount,
            FamilyCount = familyCount,
            TaggedCount = taggedCount,
            ActiveFriends = activeFriends,
            IsNewUser = ownPostCount == 0,
            OnThisDay = onThisDay
        };

        ViewBag.TaggableFriends = friendList.Friends.Select(f => new TaggableFriendViewModel
        {
            UserId = f.User.Id,
            DisplayName = f.User.DisplayName ?? f.User.UserName!
        }).ToList();

        return View(vm);
    }

    // GET: /Home/FeedPage?before=<iso>&take=20
    // Returns rendered HTML for the next page of feed cards, used by
    // the home feed's "Load more" / infinite-scroll JS. Cursor is the
    // timestamp (UTC ISO) of the oldest visible post; we return up
    // to `take` posts strictly older than that. No evergreen sprinkle
    // on follow-up pages — the first render owns that.
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> FeedPage(string? before = null, int take = 20)
    {
        if (!DateTime.TryParse(before, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cursor))
        {
            cursor = DateTime.UtcNow;
        }
        if (cursor.Kind == DateTimeKind.Unspecified) cursor = DateTime.SpecifyKind(cursor, DateTimeKind.Utc);
        if (cursor.Kind == DateTimeKind.Local) cursor = cursor.ToUniversalTime();
        take = Math.Clamp(take, 5, 50);

        var userId = _userManager.GetUserId(User)!;
        var currentUser = await _userManager.FindByIdAsync(userId);

        // Reuse the same visibility-aware feed loader; it tops out around
        // 150 rows. We then filter by cursor and apply the user/admin
        // mute toggles before rendering.
        var allFeed = await _postService.GetFeedPostsAsync(userId);

        var channelsEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.ChannelsEnabled, true);
        var biographicalEnabled = await _siteSettings.GetBoolAsync(ISiteSettings.BiographicalEnabled, true);

        var mutedChannelSet = new HashSet<int>();
        if (channelsEnabled && currentUser?.HideChannelsInFeed != true && !string.IsNullOrEmpty(currentUser?.MutedChannelIds))
        {
            foreach (var p in currentUser!.MutedChannelIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(p.Trim(), out var n)) mutedChannelSet.Add(n);
        }
        var mutedBioSet = new HashSet<string>(StringComparer.Ordinal);
        if (biographicalEnabled && currentUser?.HideBiographicalInFeed != true && !string.IsNullOrEmpty(currentUser?.MutedBiographicalUserIds))
        {
            foreach (var p in currentUser!.MutedBiographicalUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                mutedBioSet.Add(p.Trim());
        }

        var page = allFeed
            .Where(p => (p.RepublishedAt ?? p.CreatedAt) < cursor)
            .Where(p => channelsEnabled || p.ChannelId == null)
            .Where(p => biographicalEnabled || p.Owner == null || !p.Owner.IsBiographical)
            .Where(p => currentUser?.HideChannelsInFeed != true || p.ChannelId == null)
            .Where(p => currentUser?.HideBiographicalInFeed != true || p.Owner == null || !p.Owner.IsBiographical)
            .Where(p => mutedChannelSet.Count == 0 || p.ChannelId == null || !mutedChannelSet.Contains(p.ChannelId.Value))
            .Where(p => mutedBioSet.Count == 0 || p.Owner == null || !p.Owner.IsBiographical || !mutedBioSet.Contains(p.OwnerUserId))
            .OrderByDescending(p => p.RepublishedAt ?? p.CreatedAt)
            .Take(take)
            .Select(p => new FeedPostViewModel
            {
                Post = p,
                LikeCount = p.Likes.Count,
                CurrentUserLiked = p.Likes.Any(l => l.UserId == userId),
                CurrentUserReaction = p.Likes.FirstOrDefault(l => l.UserId == userId)?.ReactionType
            })
            .ToList();

        ViewData["CurrentUserId"] = userId;
        DateTime? nextCursor = page.Count > 0
            ? page.Min(p => p.Post.RepublishedAt ?? p.Post.CreatedAt)
            : (DateTime?)null;
        ViewData["NextCursor"] = nextCursor?.ToUniversalTime().ToString("o") ?? "";
        ViewData["EndOfFeed"] = page.Count < take;

        return View("FeedPage", page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Feedback(string body)
    {
        // Same AJAX protocol as Inbox/Send: XHR header or Accept: json gets a
        // JSON response, plain form post still gets the redirect-and-flash.
        var wantsJson = Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                        || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(body))
        {
            if (wantsJson) return BadRequest(new { error = "Please enter a message." });
            TempData["FeedbackError"] = "Please enter a message.";
            return RedirectToAction("Index");
        }

        var userId = _userManager.GetUserId(User)!;
        var admin = await _userManager.FindByNameAsync("kronoadmin");
        if (admin == null)
        {
            if (wantsJson) return StatusCode(503, new { error = "Feedback inbox is not available right now." });
            TempData["FeedbackError"] = "Feedback inbox is not available right now.";
            return RedirectToAction("Index");
        }

        try
        {
            _db.Messages.Add(new Message
            {
                SenderUserId = userId,
                RecipientUserId = admin.Id,
                Body = "[Feedback] " + body.Trim(),
                SentAt = DateTime.UtcNow,
                IsRead = false
            });
            await _db.SaveChangesAsync();
            if (wantsJson) return Json(new { ok = true });
            TempData["FeedbackSuccess"] = "Thanks! Your feedback was sent to Kronoadmin.";
        }
        catch (Exception ex)
        {
            if (wantsJson) return StatusCode(500, new { error = "Could not send feedback: " + ex.Message });
            TempData["FeedbackError"] = "Could not send feedback: " + ex.Message;
        }

        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Invite(int? profileId = null, int? postId = null)
    {
        var me = await _userManager.GetUserAsync(User);
        var fullName = (string.IsNullOrWhiteSpace(me?.FirstName) && string.IsNullOrWhiteSpace(me?.LastName))
            ? (me?.DisplayName ?? me?.UserName ?? "A friend")
            : ($"{me?.FirstName} {me?.LastName}").Trim();

        var vm = new InviteViewModel
        {
            Subject = $"{fullName} invites you to Kronoscript",
            Mode = "send"
        };

        // Pre-fill from a tagged-profile + post pair. The "invite to add
        // their version" CTA on the post Detail page hands us these so the
        // form arrives with the recipient's email and a story-aware message
        // already written. The author can still edit before sending.
        if (profileId.HasValue)
        {
            var pp = await _db.PersonProfiles.FirstOrDefaultAsync(p =>
                p.Id == profileId.Value && p.CreatorUserId == me!.Id);
            if (pp != null && string.IsNullOrEmpty(pp.LinkedUserId))
            {
                if (!string.IsNullOrEmpty(pp.ContactEmail))
                {
                    vm.Email = pp.ContactEmail;
                }
                if (postId.HasValue)
                {
                    var post = await _db.LifeEventPosts.FirstOrDefaultAsync(x =>
                        x.Id == postId.Value && x.OwnerUserId == me!.Id);
                    if (post != null)
                    {
                        var firstName = !string.IsNullOrWhiteSpace(pp.DisplayName)
                            ? pp.DisplayName.Split(' ')[0]
                            : "you";
                        var storyHint = !string.IsNullOrWhiteSpace(post.Title)
                            ? $"\"{post.Title}\""
                            : (post.EventYear > 0
                                ? $"the time around {post.EventYear}"
                                : "a memory I just wrote");
                        vm.Subject = $"{fullName} wrote about you on Kronoscript";
                        vm.Message =
                            $"Hi {firstName} — I just wrote a memory about {storyHint}, and you were part of it. " +
                            $"I'd love to read your version of it too. " +
                            $"Kronoscript is where I'm keeping these — join and add yours?";
                    }
                }
                else
                {
                    var firstName = !string.IsNullOrWhiteSpace(pp.DisplayName)
                        ? pp.DisplayName.Split(' ')[0]
                        : "you";
                    vm.Message =
                        $"Hi {firstName} — I've been writing memories on Kronoscript and you keep " +
                        $"showing up in them. Want to add your side? It would mean a lot.";
                }
            }
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var me = await _userManager.GetUserAsync(User);
        var fullName = (string.IsNullOrWhiteSpace(me?.FirstName) && string.IsNullOrWhiteSpace(me?.LastName))
            ? (me?.DisplayName ?? me?.UserName ?? "A friend")
            : ($"{me?.FirstName} {me?.LastName}").Trim();
        var token = Guid.NewGuid().ToString("N");

        var invitation = new Invitation
        {
            Token = token,
            InviterUserId = userId,
            InviteeEmail = model.Email,
            Message = model.Message,
            CreatedAt = DateTime.UtcNow
        };
        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync();

        // Analytics: 'invite.sent'. Tracks both send-email and link modes.
        await _analytics.RecordAsync("invite.sent", userId, new
        {
            mode = model.Mode,
            hasMessage = !string.IsNullOrWhiteSpace(model.Message)
        });

        var inviteUrl = $"{Request.Scheme}://{Request.Host}/Account/Register?invite={token}";

        if (string.Equals(model.Mode, "send", StringComparison.OrdinalIgnoreCase))
        {
            var subject = string.IsNullOrWhiteSpace(model.Subject)
                ? $"{fullName} invites you to Kronoscript"
                : model.Subject.Trim();

            var personalNote = System.Net.WebUtility.HtmlEncode(model.Message ?? "").Replace("\n", "<br/>");
            var html =
                $"<div style=\"font-family:'Segoe UI',Arial,sans-serif;color:#1a1f2e;max-width:560px;margin:0 auto;\">" +
                $"<h2 style=\"font-family:Georgia,serif;color:#0f6466;\">You've been invited to Kronoscript</h2>" +
                $"<p><strong>{System.Net.WebUtility.HtmlEncode(fullName)}</strong> wants to write your shared story together.</p>" +
                $"<blockquote style=\"border-left:3px solid #b8871a;padding:8px 14px;color:#444;background:#fafaf7;\">{personalNote}</blockquote>" +
                $"<p style=\"text-align:center;margin:28px 0;\">" +
                $"<a href=\"{inviteUrl}\" style=\"background:#0f6466;color:#fff;padding:12px 28px;border-radius:10px;text-decoration:none;font-weight:600;\">Accept &amp; Join Kronoscript</a>" +
                $"</p>" +
                $"<p style=\"font-size:0.85rem;color:#6b7280;\">Or paste this link in your browser:<br/><a href=\"{inviteUrl}\">{inviteUrl}</a></p>" +
                $"<hr style=\"border:none;border-top:1px solid #e6e6df;margin:24px 0;\"/>" +
                $"<p style=\"font-size:0.78rem;color:#9ca3af;\">Kronoscript &mdash; Live it today. Remember it forever. Tell it together.</p>" +
                $"</div>";

            try
            {
                await _emailSender.SendEmailAsync(model.Email, subject, html);
                TempData["Success"] = $"Invite sent to {model.Email}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invite email failed for {Email}", model.Email);
                TempData["Error"] = "Could not send the email. Try the shareable link option instead.";
                TempData["InviteToken"] = token;
                TempData["InviteEmail"] = model.Email;
                TempData["InviteMessage"] = model.Message;
                return RedirectToAction("InviteSent");
            }

            return RedirectToAction("Invite");
        }

        // Mode: link (just generate the shareable link page)
        TempData["InviteToken"] = token;
        TempData["InviteEmail"] = model.Email;
        TempData["InviteMessage"] = model.Message;
        return RedirectToAction("InviteSent");
    }

    [HttpGet]
    public IActionResult InviteSent()
    {
        ViewBag.Token = TempData["InviteToken"];
        ViewBag.Email = TempData["InviteEmail"];
        ViewBag.Message = TempData["InviteMessage"];
        return View();
    }

    [HttpGet]
    public IActionResult GettingStarted() => View();

    public IActionResult Privacy() => View();
    public IActionResult About() => View();
    public IActionResult Agreement() => View();
    public IActionResult AcceptableUse() => View();
    public IActionResult Faq() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
