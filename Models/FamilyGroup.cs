using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// An ad-hoc family grouping ("Descendants of great-grandfather X",
/// "My kids + wife only", "My in-laws"). Premium Family-tier feature.
/// A user can belong to many groups; a group has one creator (Admin),
/// optional Co-Admins, and Members. Posts are M:N with groups via
/// <see cref="FamilyGroupPost"/>.
/// </summary>
public class FamilyGroup
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

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
