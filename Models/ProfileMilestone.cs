using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>Discrete closeness level at a moment in time. Each kind
/// is BOTH the event ("we got close") and the resulting band on the
/// Friendship graph (top to bottom: Best, Close, Friend, Connected,
/// Drifted, Estranged, Lost contact). Between milestones the line
/// holds the previous level (step function).
///
/// Integer values intentionally start at 10 so the migration from the
/// old 0..5 enum (Met / Close / Drifted / Estranged / Reconnected /
/// Lost) is idempotent — the remap target (10..16) never collides
/// with a source value, so re-running the data migration is a no-op
/// on already-migrated rows.</summary>
public enum ProfileMilestoneKind
{
    /// <summary>Best — top of the spectrum (+3).</summary>
    Best = 10,

    /// <summary>Close — (+2).</summary>
    Close = 11,

    /// <summary>Friend — (+1). Default starting point for a new
    /// relationship; old "Met" and "Reconnected" both map here.</summary>
    Friend = 12,

    /// <summary>Connected — acquaintance-tier, in touch but not
    /// close (0).</summary>
    Connected = 13,

    /// <summary>Drifted — (-1).</summary>
    Drifted = 14,

    /// <summary>Estranged — (-2).</summary>
    Estranged = 15,

    /// <summary>Lost contact — bottom of the spectrum (-3).
    /// Differs from the old "Lost" kind: the line stays at -3 rather
    /// than terminating.</summary>
    LostContact = 16
}

/// <summary>A single point on the relationship-arc timeline of a
/// PersonProfile. Tied to <see cref="PersonProfile"/> rather than the
/// linked member (if any) so the data lives with the NPC card the
/// creator authored. Editing is creator-only (same authority as the
/// profile itself).</summary>
public class ProfileMilestone
{
    public int Id { get; set; }

    public int PersonProfileId { get; set; }

    [ForeignKey(nameof(PersonProfileId))]
    public PersonProfile? PersonProfile { get; set; }

    /// <summary>Calendar year the milestone happened. Year-grain is
    /// good enough for a friendship arc — month/day adds clutter
    /// without helping the chart.</summary>
    public int Year { get; set; }

    public ProfileMilestoneKind Kind { get; set; }

    /// <summary>Short free-text colour ("started at the same job",
    /// "after Lisa's wedding") — optional, surfaced as a tooltip on
    /// the chart point.</summary>
    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
