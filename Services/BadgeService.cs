using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Helpers;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public record LadderProgress(
    string Key,
    string Name,
    string CountUnit,
    long Count,
    int CurrentLevel,
    string CurrentTitle,
    long? NextThreshold,
    string? NextTitle,
    string BadgeImageUrl,
    int ProgressPercent);

public interface IBadgeService
{
    Task<List<LadderProgress>> GetProgressAsync(string userId, CancellationToken ct = default);
}

public class BadgeService : IBadgeService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    private const int MinPostWords = 50;
    private const int MinCommentWords = 10;

    public BadgeService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<List<LadderProgress>> GetProgressAsync(string userId, CancellationToken ct = default)
    {
        // Posts + words: load body lengths once and compute both metrics from the same set.
        var ownPostBodies = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId && !p.IsDraft)
            .Select(p => p.Body ?? "")
            .ToListAsync(ct);

        long postsCount = 0;
        long wordsTotal = 0;
        foreach (var body in ownPostBodies)
        {
            var words = WordCount(body);
            wordsTotal += words;
            if (words >= MinPostWords) postsCount++;
        }

        // Connections: accepted FriendConnections in either direction.
        var connectionsCount = await _db.FriendConnections.CountAsync(
            c => c.Status == FriendConnectionStatus.Accepted
              && (c.RequesterUserId == userId || c.AddresseeUserId == userId), ct);

        // Comments: authored by me on someone else's post, with a minimum word count.
        // EF can't run a managed-code WordCount in SQL, so pull bodies and count in memory.
        var commentBodies = await _db.Comments
            .Where(c => c.AuthorUserId == userId)
            .Where(c => c.Post.OwnerUserId != userId)
            .Select(c => c.Body ?? "")
            .ToListAsync(ct);
        long commentsCount = commentBodies.Count(b => WordCount(b) >= MinCommentWords);

        // Logins: pre-aggregated count, updated by LastSeenMiddleware.
        var user = await _userManager.FindByIdAsync(userId);
        long loginsCount = user?.LoginDaysCount ?? 0;

        return new List<LadderProgress>
        {
            BuildProgress(BadgeLadders.Posts,       postsCount),
            BuildProgress(BadgeLadders.Words,       wordsTotal),
            BuildProgress(BadgeLadders.Connections, connectionsCount),
            BuildProgress(BadgeLadders.Comments,    commentsCount),
            BuildProgress(BadgeLadders.Logins,      loginsCount)
        };
    }

    private static LadderProgress BuildProgress(BadgeLadders.Ladder ladder, long count)
    {
        // Find the highest tier whose threshold is satisfied (0 if none).
        BadgeLadders.Tier? earned = null;
        foreach (var tier in ladder.Tiers)
        {
            if (count >= tier.Threshold) earned = tier;
            else break;
        }

        var currentLevel = earned?.Level ?? 0;
        var currentTitle = earned?.Title ?? "Not yet earned";
        var nextTier = ladder.Tiers.FirstOrDefault(t => t.Level == currentLevel + 1);
        var badgeImage = currentLevel > 0
            ? $"/badges/{ladder.Key}-{currentLevel:D2}.png"
            : $"/badges/{ladder.Key}-01.png"; // show the first badge faded as a target

        int pct;
        if (nextTier == null)
        {
            pct = 100; // maxed out
        }
        else
        {
            var floor = earned?.Threshold ?? 0;
            var span = nextTier.Threshold - floor;
            pct = span > 0 ? (int)Math.Clamp(100 * (count - floor) / span, 0, 100) : 100;
        }

        return new LadderProgress(
            Key: ladder.Key,
            Name: ladder.Name,
            CountUnit: ladder.CountUnit,
            Count: count,
            CurrentLevel: currentLevel,
            CurrentTitle: currentTitle,
            NextThreshold: nextTier?.Threshold,
            NextTitle: nextTier?.Title,
            BadgeImageUrl: badgeImage,
            ProgressPercent: pct);
    }

    /// <summary>Naive whitespace word count. Good enough for badge thresholds.</summary>
    private static int WordCount(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var n = 0;
        var inWord = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                if (inWord) { n++; inWord = false; }
            }
            else
            {
                inWord = true;
            }
        }
        if (inWord) n++;
        return n;
    }
}
