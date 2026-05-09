using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public interface ISiteSettings
{
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task SetBoolAsync(string key, bool value);

    // Well-known keys (admin Site Settings page binds these directly).
    const string ChannelsEnabled = "ChannelsEnabled";
    const string BiographicalEnabled = "BiographicalEnabled";
    const string EvergreenSurfacing = "EvergreenSurfacing";
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
        var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            row = new SiteSetting { Key = key };
            _db.SiteSettings.Add(row);
        }
        row.Value = value ? "true" : "false";
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        // Bust cache so admins see the new value immediately on the next page.
        _cache.Set(CacheKey(key), (bool?)value, CacheTtl);
    }
}
