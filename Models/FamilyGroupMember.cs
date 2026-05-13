using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// Membership row for a <see cref="FamilyGroup"/>. Unique on
/// (FamilyGroupId, UserId) — a user can't have two roles in the same
/// group. CoAdmin promotion requires the target to have an active
/// premium subscription (enforced at controller level).
/// </summary>
public class FamilyGroupMember
{
    public int Id { get; set; }

    public int FamilyGroupId { get; set; }
    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    [Required]
    public string UserId { get; set; } = "";
    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    public FamilyGroupRole Role { get; set; } = FamilyGroupRole.Member;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
