using Microsoft.EntityFrameworkCore;
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
    public SiteSettingsService(ApplicationDbContext db) { _db = db; }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        try
        {
            var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (row?.Value == null) return defaultValue;
            return string.Equals(row.Value, "true", StringComparison.OrdinalIgnoreCase)
                || row.Value == "1";
        }
        catch
        {
            // SiteSettings table may not exist yet on first boot; fall back
            // to the default so admin features stay reachable.
            return defaultValue;
        }
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
    }
}
