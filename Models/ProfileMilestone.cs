using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>Discrete event in a relationship's arc. Plotted as a step
/// on the Friendship graph — the Y position is the closeness band the
/// kind maps to, and the X position is the year. Between milestones
/// the line holds the previous level (step function).</summary>
public enum ProfileMilestoneKind
{
    /// <summary>First met — anchors the start of the relationship.
    /// Defaults the closeness level to <c>Friend</c> (+1).</summary>
    Met = 0,

    /// <summary>Became close — Best-friend band (+3).</summary>
    Close = 1,

    /// <summary>Drifted apart but not estranged — Acquaintance band (0).</summary>
    Drifted = 2,

    /// <summary>Fell out, conscious distance — Estranged band (-2).</summary>
    Estranged = 3,

    /// <summary>Reconnected after distance — Friend band (+1).</summary>
    Reconnected = 4,

    /// <summary>Lost touch entirely or passed away — line ends.</summary>
    Lost = 5
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
