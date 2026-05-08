using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// Topical channel created by an app admin. Posts in a channel are visually
/// distinct on the home feed (yellow accent + channel badge). Only the
/// assigned <see cref="AdminUserId"/> (or any app-admin) can post into it;
/// everyone can comment / react.
/// </summary>
public class Channel
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-safe slug; used for /Channel/{slug} (future) and channel filters.</summary>
    [Required, MaxLength(80)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Single emoji or short symbol shown on the channel badge (e.g. "📚").</summary>
    [MaxLength(8)]
    public string? IconEmoji { get; set; }

    /// <summary>The user assigned to write content in this channel. Null = only app admins can post.</summary>
    public string? AdminUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public string CreatedByUserId { get; set; } = null!;

    [ForeignKey(nameof(AdminUserId))]
    public ApplicationUser? Admin { get; set; }
}
