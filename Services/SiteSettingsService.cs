using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public interface ISiteSettings
{
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task SetBoolAsync(string key, bool value);
    Task<string?> GetStringAsync(string key, string? defaultValue = null);
    Task SetStringAsync(string key, string? value);
    Task<int> GetIntAsync(string key, int defaultValue = 0);
    Task SetIntAsync(string key, int value);

    // Well-known keys (admin Site Settings page binds these directly).
    const string ChannelsEnabled = "ChannelsEnabled";
    const string BiographicalEnabled = "BiographicalEnabled";
    const string EvergreenSurfacing = "EvergreenSurfacing";

    // Evergreen surfacing rules — control how often, where, and how
    // channel + biographical posts re-appear in the home feed. Each
    // category (channel / bio) has its own knobs so admins can tune
    // them independently.
    //
    // Position: "top" | "middle" | "random" — where evergreen picks land
    //   in the feed. "top" pins them above the fold, "middle" inserts
    //   around the visible-fold area, "random" spreads them through the
    //   first ~18 posts (the legacy behavior).
    // Order: "random" | "recent" — pick strategy. "random" shuffles the
    //   pool, "recent" prefers newer posts.
    // MaxPerPage: 0 disables, otherwise the cap on inserts per page.
    // AllowBackToBack: false → walker spreads picks so no two evergreen
    //   inserts of this kind sit adjacent.
    // DailyOnePerSource: true → at most one pick per channel (or per
    //   biographical user) per user per day, so one source can't hog
    //   the rotation.
    const string EvergreenChannelMaxPerPage = "EvergreenChannelMaxPerPage";
    const string EvergreenChannelPosition = "EvergreenChannelPosition";
    const string EvergreenChannelOrder = "EvergreenChannelOrder";
    const string EvergreenChannelAllowBackToBack = "EvergreenChannelAllowBackToBack";
    const string EvergreenChannelDailyOnePerSource = "EvergreenChannelDailyOnePerSource";
    const string EvergreenBioMaxPerPage = "EvergreenBioMaxPerPage";
    const string EvergreenBioPosition = "EvergreenBioPosition";
    const string EvergreenBioOrder = "EvergreenBioOrder";
    const string EvergreenBioAllowBackToBack = "EvergreenBioAllowBackToBack";
    const string EvergreenBioDailyOnePerSource = "EvergreenBioDailyOnePerSource";

    // Site banner — top-of-page strip for operational announcements.
    const string BannerEnabled = "BannerEnabled";
    const string BannerText = "BannerText";
    const string BannerSeverity = "BannerSeverity"; // "info" | "warning" | "critical"
    const string BannerLinkUrl = "BannerLinkUrl";
    const string BannerLinkText = "BannerLinkText";
    const string BannerVersion = "BannerVersion";   // bumped on edit; users compare LastDismissedBannerVersion

    // What's new modal — shown once per user when version increments.
    const string WhatsNewEnabled = "WhatsNewEnabled";
    const string WhatsNewTitle = "WhatsNewTitle";
    const string WhatsNewBody = "WhatsNewBody";
    const string WhatsNewVersion = "WhatsNewVersion";

    // Creator's notes — private scratchpad on the Site Settings page,
    // visible only to admins. One item per line; the textarea is the
    // editor. Used to track what's planned for premium tiers.
    const string CreatorNotesPremiumFeatures = "CreatorNotesPremiumFeatures";
    const string CreatorNotesPremiumServices = "CreatorNotesPremiumServices";
}

public class SiteSettingsService : ISiteSettings
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public SiteSettingsService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string CacheKey(string key) => "sitesetting:" + key;

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var cacheKey = CacheKey(key);
        if (_cache.TryGetValue<bool?>(cacheKey, out var cached) && cached.HasValue)
        {
            return cached.Value;
        }
        bool value;
        try
        {
            var row = await _db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
            if (row?.Value == null)
            {
                value = defaultValue;
            }
            else
            {
                value = string.Equals(row.Value, "true", StringComparison.OrdinalIgnoreCase)
                    || row.Value == "1";
            }
        }
        catch
        {
            // SiteSettings table may not exist yet on first boot; fall back
            // to the default so admin features stay reachable.
            value = defaultValue;
        }
        _cache.Set(cacheKey, (bool?)value, CacheTtl);
        return value;
    }

    public async Task SetBoolAsync(string key, bool value)
    {
        await UpsertRawAsync(key, value ? "true" : "false");
        _cache.Set(CacheKey(key), (bool?)value, CacheTtl);
    }

    public async Task<string?> GetStringAsync(string key, string? defaultValue = null)
    {
        var cacheKey = CacheKey(key);
        if (_cache.TryGetValue<string?>(cacheKey, out var cached))
        {
            return cached ?? defaultValue;
        }
        string? value = defaultValue;
        try
        {
            var row = await _db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
            value = row?.Value ?? defaultValue;
        }
        catch { value = defaultValue; }
        _cache.Set(cacheKey, value, CacheTtl);
        return value;
    }

    public async Task SetStringAsync(string key, string? value)
    {
        await UpsertRawAsync(key, value);
        _cache.Set(CacheKey(key), value, CacheTtl);
    }

    public async Task<int> GetIntAsync(string key, int defaultValue = 0)
    {
        var s = await GetStringAsync(key);
        if (int.TryParse(s, out var n)) return n;
        return defaultValue;
    }

    public async Task SetIntAsync(string key, int value)
    {
        await SetStringAsync(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task UpsertRawAsync(string key, string? value)
    {
        var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            row = new SiteSetting { Key = key };
            _db.SiteSettings.Add(row);
        }
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
