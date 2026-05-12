using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// A profile for someone the user writes about who isn't a Kronoscript
/// member — deceased relatives, distant family, anyone who'll never
/// sign up. Once created, the profile can be tagged in stories and
/// photos the same way real members can. If the person later joins,
/// the creator can link the profile to their real account, which
/// merges all tag references into the new user.
///
/// Premium-gated to *create*. Existing profiles stay viewable and
/// taggable even if the creator's subscription lapses (deletion of
/// information already in stories would surprise users badly).
/// </summary>
public class PersonProfile
{
    public int Id { get; set; }

    /// <summary>The user who created and owns this profile. Only the
    /// creator (and admins) can edit it. Reading respects Visibility.</summary>
    public string CreatorUserId { get; set; } = null!;

    [ForeignKey(nameof(CreatorUserId))]
    public ApplicationUser? Creator { get; set; }

    [Required]
    [MaxLength(120)]
    public string DisplayName { get; set; } = null!;

    /// <summary>What the family actually calls this person — "Nonno",
    /// "Zio Bobby", "Aunt Lou". Shown as the primary label on the
    /// Family Tree bubble (with the full name as the subtitle) so the
    /// tree reads the way the family talks.</summary>
    [MaxLength(80)]
    public string? Nickname { get; set; }

    /// <summary>"Male", "Female", or null/empty. Used by the kinship
    /// calculator to pick the right gendered term (Grandfather vs
    /// Grandmother, Niece vs Nephew). Auto-set to Male/Female when the
    /// profile is created via the popup's "+ Father" or "+ Mother"
    /// button; otherwise the user picks it on the profile form.</summary>
    [MaxLength(20)]
    public string? Gender { get; set; }

    /// <summary>Free-text relation to the creator — "Mother",
    /// "Best friend at the bottega", "Great-grandfather". Suggestion
    /// chips in the form prompt the common ones but anyone can type
    /// anything.</summary>
    [MaxLength(80)]
    public string? Relation { get; set; }

    /// <summary>URL of the bubble avatar — uploaded image, snapped
    /// crop from an existing tagged photo, or null (in which case
    /// the view renders an initials circle).</summary>
    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    public int? BirthYear { get; set; }
    [MaxLength(120)]
    public string? BirthPlace { get; set; }
    public int? DeathYear { get; set; }
    [MaxLength(120)]
    public string? DeathPlace { get; set; }

    /// <summary>True when the birth/death years are approximate ("ca.").
    /// Renders the same way EventDateIsEstimated does on posts.</summary>
    public bool DatesEstimated { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary>Optional "where I got this from" field — useful for
    /// genealogy-minded users tracking family records.</summary>
    [MaxLength(500)]
    public string? Sources { get; set; }

    /// <summary>Who can see this profile. Same enum as posts so the
    /// existing CanViewPostsAsync check works without a new ladder.
    /// Defaults to Family which is the right cautious default for
    /// info about non-consenting third parties.</summary>
    public PostVisibility Visibility { get; set; } = PostVisibility.Family;

    /// <summary>Optional email — used by the claim flow. When someone
    /// signs up with this email, a banner offers them to claim the
    /// profile. We never email this address from this column; the
    /// match happens passively at signup.</summary>
    [MaxLength(256)]
    public string? ContactEmail { get; set; }

    /// <summary>Filled in once the profile is linked to a real member
    /// account. From then on, tags rendered as a link to the real
    /// member's timeline instead of the standalone profile page.
    /// Settable only via the claim flow (the matched email's owner
    /// confirms) or by an admin/creator/linked-user unlink action.</summary>
    public string? LinkedUserId { get; set; }

    [ForeignKey(nameof(LinkedUserId))]
    public ApplicationUser? LinkedUser { get; set; }

    /// <summary>Set when the email-match owner clicks "Yes, that's me".</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>Set when the email-match owner clicks "Not me". Suppresses
    /// the banner from re-appearing on every page load. Cleared if the
    /// creator edits the profile (giving them a way to re-surface a
    /// dismissed claim — e.g. fixed typo in the email).</summary>
    public DateTime? ClaimDeclinedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Convenience: combined birth/death range for byline
    /// rendering. "1932 – 2017" / "1932 – present" / "ca. 1890 – 1955".</summary>
    [NotMapped]
    public string LifeRange
    {
        get
        {
            if (BirthYear == null && DeathYear == null) return "";
            var prefix = DatesEstimated ? "ca. " : "";
            var b = BirthYear?.ToString() ?? "?";
            var d = DeathYear?.ToString() ?? "present";
            return $"{prefix}{b} – {d}";
        }
    }
}
