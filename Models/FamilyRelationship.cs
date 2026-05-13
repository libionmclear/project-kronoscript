using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum FamilyRelationType
{
    /// <summary>From is parent of To. Inverse (child) is implicit.</summary>
    Parent = 0,

    /// <summary>Symmetric — stored once, From/To order is arbitrary.</summary>
    Spouse = 1,

    /// <summary>Symmetric. Used when both parents aren't on the tree but
    /// the family connection still matters (e.g. "my mother's sister
    /// whose parents I never met"). Stored once, From/To order
    /// arbitrary. Half-siblings can be expressed by adding a Sibling
    /// edge plus a single shared Parent edge instead of two.</summary>
    Sibling = 2
}

/// <summary>
/// An edge between two nodes on a single owner's family tree.
/// Stored per-tree (OwnerUserId) — so two members building their
/// own trees can express different relationships for the same pair
/// without colliding.
/// </summary>
public class FamilyRelationship
{
    public int Id { get; set; }

    [Required]
    public string OwnerUserId { get; set; } = null!;

    /// <summary>When set, this edge belongs to a <see cref="FamilyGroup"/>'s
    /// shared tree rather than a personal tree. Matches the same field on
    /// <see cref="FamilyTreeNode"/>.</summary>
    public int? FamilyGroupId { get; set; }

    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    public int FromNodeId { get; set; }

    [ForeignKey(nameof(FromNodeId))]
    public FamilyTreeNode? FromNode { get; set; }

    public int ToNodeId { get; set; }

    [ForeignKey(nameof(ToNodeId))]
    public FamilyTreeNode? ToNode { get; set; }

    public FamilyRelationType RelType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
