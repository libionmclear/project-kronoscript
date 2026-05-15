using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

/// <summary>
/// First-class analytics event row — append-only log of business
/// events the team wants to query post-launch. Separate from
/// Application Insights (which captures performance + errors) so we
/// can run plain SQL against it from the Admin dashboard without
/// involving a third party.
///
/// EventType is a free-form string keyed by convention:
///   register, post.published, invite.sent, share.clicked, login.day,
///   referral.signup, etc.
///
/// EventData is optional JSON payload (Postgres jsonb) — small enough
/// to dump straight to the admin grid, structured enough to mine for
/// dimensions later.
/// </summary>
public class UserEvent
{
    public long Id { get; set; }

    /// <summary>Actor user id when known. Null for anonymous events
    /// (e.g., share-link click before signup) — those still log so we
    /// can attribute funnel drop-off.</summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>Stable event key, lowercase dot-separated.</summary>
    [Required, MaxLength(80)]
    public string EventType { get; set; } = "";

    /// <summary>Free-form JSON payload — Postgres jsonb column. Stays
    /// nullable so a bare 'fact happened' event costs nothing.</summary>
    public string? EventData { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
