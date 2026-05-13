using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum RelationshipType
{
    Spouse,
    Parent,
    Child,
    Sibling,
    Aunt,
    Uncle,
    Cousin,
    Grandparent,
    Grandchild,
    InLaw,
    Other
}

public enum RelativeConnectionStatus
{
    Pending,
    Accepted
}

public class RelativeConnection
{
    public int Id { get; set; }

    [Required]
    public string UserAId { get; set; } = null!;

    [Required]
    public string UserBId { get; set; } = null!;

    public RelationshipType RelationshipType { get; set; }
    public RelativeConnectionStatus Status { get; set; } = RelativeConnectionStatus.Pending;

    /// <summary>Optional year — used when RelationshipType is Spouse so
    /// the marriage year shows up on both linked profiles. Nullable for
    /// every other relationship and for spouses without a recorded date.</summary>
    public int? MarriageYear { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserAId))]
    public ApplicationUser UserA { get; set; } = null!;

    [ForeignKey(nameof(UserBId))]
    public ApplicationUser UserB { get; set; } = null!;
}
