using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

/// <summary>Tiny key/value store for runtime-tunable site flags (admin
/// toggles like "channels enabled", "biographical enabled"). Backed by a
/// table so changes persist across app restarts; reads cached briefly in
/// SiteSettingsService.</summary>
public class SiteSetting
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
