using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// An ad-hoc grouping users assemble for shared storytelling. Originally
/// "Family Group" but groups are not necessarily family — see <see cref="Kind"/>.
/// Premium Family-tier feature. A user can belong to many groups; a group
/// has one creator (Admin), optional Co-Admins, and Members. Posts are
/// M:N with groups via <see cref="FamilyGroupPost"/>. The class is still
/// named FamilyGroup for historical/DB reasons; user-facing copy says
/// "Group".
/// </summary>
public class FamilyGroup
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>What flavor of group this is — informs default permissions,
    /// invite copy, and whether family-tree integration shows up. Existing
    /// rows default to <see cref="GroupKind.Family"/>.</summary>
    public GroupKind Kind { get; set; } = GroupKind.Family;

    /// <summary>Creator is the founding Admin. Membership in
    /// <see cref="FamilyGroupMember"/> reflects this redundantly so the
    /// role check is one query against one table.</summary>
    [Required]
    public string CreatorUserId { get; set; } = "";
    [ForeignKey(nameof(CreatorUserId))]
    public ApplicationUser? Creator { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<FamilyGroupMember> Members { get; set; } = new List<FamilyGroupMember>();
    public ICollection<FamilyGroupPost> Posts { get; set; } = new List<FamilyGroupPost>();
}

public enum FamilyGroupRole
{
    Member  = 0,
    CoAdmin = 1,
    Admin   = 2
}

/// <summary>
/// The flavor of a group. Drives label copy and (in the future) which
/// integrations are enabled — e.g. Family groups can pin a shared
/// family-tree snapshot, Friends groups can't.
/// </summary>
public enum GroupKind
{
    Family = 0,
    Friends = 1,
    Mixed = 2
}
