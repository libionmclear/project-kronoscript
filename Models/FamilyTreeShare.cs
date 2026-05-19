using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// Live link between a user's personal family tree and a Family Group's
/// shared canvas. Unlike <see cref="FamilyTreeNode.FamilyGroupId"/>
/// (which is a hard "this node belongs to a group" assignment), a Share
/// is a POINTER: when a member views the group's tree they actually see
/// the linked user's personal tree. Edits the linked user makes to
/// their own tree flow through to the group view automatically.
///
/// Exactly one share per (group, user) — composite unique index.
/// </summary>
public class FamilyTreeShare
{
    public int Id { get; set; }

    /// <summary>Owner of the personal tree being shared into the group.
    /// Their personal tree nodes (FamilyGroupId == null) become visible
    /// on the group's tree page for every group member.</summary>
    [Required]
    public string UserId { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    public int FamilyGroupId { get; set; }

    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
