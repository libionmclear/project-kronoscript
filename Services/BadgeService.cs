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
    int ProgressPercent,
    bool IsNewlyEarned);

/// <summary>One-shot founding-member badge based on signup order (rank).</summary>
public record FoundingBadge(
    string Title,
    string Tagline,
    string ImageUrl,
    int Ordinal,
    bool IsNewlyEarned);

public interface IBadgeService
{
    Task<List<LadderProgress>> GetProgressAsync(string userId, CancellationToken ct = default);
    Task<FoundingBadge?> GetFoundingBadgeAsync(string userId, CancellationToken ct = default);
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
        var commentBodies = await _db.Comments
            .Where(c => c.AuthorUserId == userId)
            .Where(c => c.Post.OwnerUserId != userId)
            .Select(c => c.Body ?? "")
            .ToListAsync(ct);
        long commentsCount = commentBodies.Count(b => WordCount(b) >= MinCommentWords);

        var user = await _userManager.FindByIdAsync(userId);
        long loginsCount = user?.LoginDaysCount ?? 0;

        var progress = new List<LadderProgress>
        {
            BuildProgress(BadgeLadders.Posts,       postsCount,       user?.LastBadgeLevelPosts       ?? 0),
            BuildProgress(BadgeLadders.Words,       wordsTotal,       user?.LastBadgeLevelWords       ?? 0),
            BuildProgress(BadgeLadders.Connections, connectionsCount, user?.LastBadgeLevelConnections ?? 0),
            BuildProgress(BadgeLadders.Comments,    commentsCount,    user?.LastBadgeLevelComments    ?? 0),
            BuildProgress(BadgeLadders.Logins,      loginsCount,      user?.LastBadgeLevelLogins      ?? 0)
        };

        // Persist any tier increases so the next dashboard view doesn't re-fire the
        // celebration. We never decrement — losing a connection / deleting a post
        // shouldn't take a badge away.
        if (user != null)
        {
            var changed = false;
            if (progress[0].CurrentLevel > user.LastBadgeLevelPosts)       { user.LastBadgeLevelPosts       = progress[0].CurrentLevel; changed = true; }
            if (progress[1].CurrentLevel > user.LastBadgeLevelWords)       { user.LastBadgeLevelWords       = progress[1].CurrentLevel; changed = true; }
            if (progress[2].CurrentLevel > user.LastBadgeLevelConnections) { user.LastBadgeLevelConnections = progress[2].CurrentLevel; changed = true; }
            if (progress[3].CurrentLevel > user.LastBadgeLevelComments)    { user.LastBadgeLevelComments    = progress[3].CurrentLevel; changed = true; }
            if (progress[4].CurrentLevel > user.LastBadgeLevelLogins)      { user.LastBadgeLevelLogins      = progress[4].CurrentLevel; changed = true; }
            if (changed)
            {
                try { await _db.SaveChangesAsync(ct); }
                catch { /* best-effort; missed level-up will just not re-fire */ }
            }
        }

        return progress;
    }

    private static LadderProgress BuildProgress(BadgeLadders.Ladder ladder, long count, int previouslyAcknowledgedLevel)
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
            ProgressPercent: pct,
            IsNewlyEarned: currentLevel > previouslyAcknowledgedLevel);
    }

    public async Task<FoundingBadge?> GetFoundingBadgeAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        // Ordinal = number of users created strictly before this one, plus 1.
        // Counted in real time; cheap at our scale.
        var earlier = await _db.Users.CountAsync(u => u.CreatedAt < user.CreatedAt, ct);
        var ordinal = earlier + 1;

        string? title = null;
        string? tagline = null;
        string? image = null;

        if (ordinal <= 100)
        {
            title = "Genesis";
            tagline = "One of the first 100 to land on Kronoscript.";
            image = "/badges/genesis.png";
        }
        else if (ordinal <= 350)
        {
            title = "Prologue";
            tagline = "Among the first 250 explorers (101–350).";
            image = "/badges/prologue.png";
        }
        else if (ordinal <= 1350)
        {
            title = "Chapter One";
            tagline = "Among the first 1,000 to write a story (351–1,350).";
            image = "/badges/chapter1.png";
        }
        else
        {
            return null; // no founding badge for later signups
        }

        var newlyEarned = !user.FoundingBadgeAcknowledged;
        if (newlyEarned)
        {
            user.FoundingBadgeAcknowledged = true;
            try { await _db.SaveChangesAsync(ct); }
            catch { /* best-effort; will just re-fire next view */ }
        }

        return new FoundingBadge(title, tagline, image, ordinal, newlyEarned);
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
