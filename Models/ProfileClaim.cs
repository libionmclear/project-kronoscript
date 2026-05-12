using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// Joiner-initiated claim on a PersonProfile (Tier 2/3 of the claim
/// authority hierarchy). A user who believes an NPC card is them
/// files a Pending claim; the profile's creator approves or denies.
/// On approval the profile gets LinkedUserId set the same way the
/// creator-initiated link works, and the joiner inherits all the
/// history attached to the card.
/// </summary>
public class ProfileClaim
{
    public int Id { get; set; }

    public int PersonProfileId { get; set; }
    [ForeignKey(nameof(PersonProfileId))]
    public PersonProfile? PersonProfile { get; set; }

    [Required]
    public string ClaimantUserId { get; set; } = null!;
    [ForeignKey(nameof(ClaimantUserId))]
    public ApplicationUser? Claimant { get; set; }

    public ProfileClaimStatus Status { get; set; } = ProfileClaimStatus.Pending;

    /// <summary>Optional free-text note the joiner can attach to their
    /// claim — useful for "this is me, here's why" context the creator
    /// can use to make the approve/deny call.</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public enum ProfileClaimStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Withdrawn = 3
}
