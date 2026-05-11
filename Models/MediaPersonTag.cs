using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// A name-label pinned to a specific spot on a PostMedia image —
/// Facebook-style face-tagging. Each tag points to either a real
/// Kronoscript member (TargetUserId) or a People Profile
/// (TargetProfileId); exactly one of the two is populated.
/// X and Y are stored as percentages of the photo's natural dimensions
/// so the label stays in the right place at any rendered size.
/// </summary>
public class MediaPersonTag
{
    public int Id { get; set; }

    public int PostMediaId { get; set; }

    [ForeignKey(nameof(PostMediaId))]
    public PostMedia? Media { get; set; }

    /// <summary>Set when the tagged person is a Kronoscript member.</summary>
    public string? TargetUserId { get; set; }

    [ForeignKey(nameof(TargetUserId))]
    public ApplicationUser? TargetUser { get; set; }

    /// <summary>Set when the tagged person is a People Profile (non-member).</summary>
    public int? TargetProfileId { get; set; }

    [ForeignKey(nameof(TargetProfileId))]
    public PersonProfile? TargetProfile { get; set; }

    /// <summary>Horizontal position as a percentage (0–100) of the photo's width.</summary>
    public double X { get; set; }

    /// <summary>Vertical position as a percentage (0–100) of the photo's height.</summary>
    public double Y { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
