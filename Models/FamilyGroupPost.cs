using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// M:N link between a <see cref="LifeEventPost"/> and a
/// <see cref="FamilyGroup"/>. A post can live on its author's personal
/// feed AND in N groups simultaneously; each group has its own surface
/// for that post (comments are scoped per surface, not shared).
/// Adding an EXISTING post to a group requires the adder to be a member
/// of the group with premium (enforced at controller level).
/// </summary>
public class FamilyGroupPost
{
    public int Id { get; set; }

    public int FamilyGroupId { get; set; }
    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    public int LifeEventPostId { get; set; }
    [ForeignKey(nameof(LifeEventPostId))]
    public LifeEventPost? LifeEventPost { get; set; }

    [Required]
    public string AddedByUserId { get; set; } = "";
    [ForeignKey(nameof(AddedByUserId))]
    public ApplicationUser? AddedBy { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
