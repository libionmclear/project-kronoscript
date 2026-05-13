using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum FamilyNodeKind
{
    /// <summary>Node points at a real Kronoscript member (AspNetUsers).
    /// Used for the owner themselves and for connected members the
    /// owner has placed on their tree.</summary>
    Member = 0,

    /// <summary>Node points at a PersonProfile (NPC card for a non-
    /// member — typically a deceased or distant relative).</summary>
    Profile = 1
}

/// <summary>
/// A draggable node on someone's personal family-tree canvas. Each
/// member has their own tree; the same target (a PersonProfile or a
/// connected member) can appear at most once per tree.
/// </summary>
public class FamilyTreeNode
{
    public int Id { get; set; }

    [Required]
    public string OwnerUserId { get; set; } = null!;

    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? Owner { get; set; }

    /// <summary>When set, this node belongs to the SHARED tree of a
    /// <see cref="FamilyGroup"/> (curated by group admins + co-admins).
    /// Personal-tree nodes leave this null and rely on OwnerUserId for
    /// scoping. For group nodes, OwnerUserId becomes the "created/last
    /// edited by" audit field rather than the tree owner.</summary>
    public int? FamilyGroupId { get; set; }

    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    public FamilyNodeKind NodeKind { get; set; }

    /// <summary>Set when NodeKind == Member.</summary>
    public string? TargetUserId { get; set; }

    [ForeignKey(nameof(TargetUserId))]
    public ApplicationUser? TargetUser { get; set; }

    /// <summary>Set when NodeKind == Profile.</summary>
    public int? TargetProfileId { get; set; }

    [ForeignKey(nameof(TargetProfileId))]
    public PersonProfile? TargetProfile { get; set; }

    /// <summary>Canvas coordinates in "world" units (the view's CSS
    /// translates this to pixels; clamped client-side to [0, 4000]).
    /// Default 0,0 lands the node in the top-left — the controller
    /// nudges new nodes onto a 6-column grid before saving.</summary>
    public double X { get; set; }
    public double Y { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
