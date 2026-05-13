using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum NotificationType
{
    Comment = 0,
    Mention = 1,
    Reply = 2,
    FriendRequest = 3,
    FriendAccepted = 4,
    Announcement = 5,
    /// <summary>Sent to the creator of a PersonProfile when the
    /// matched-email member confirms "yes, that's me" and claims it.</summary>
    ProfileClaimed = 6,
    /// <summary>Sent to the creator when a joiner files a claim
    /// request on one of their NPC profiles (Tier 2/3 hierarchy).</summary>
    ProfileClaimRequested = 7,
    /// <summary>Sent to the claimant when the creator approves their
    /// claim request — the profile is now linked to them.</summary>
    ProfileClaimApproved = 8,
    /// <summary>Sent to the claimant when the creator denies.</summary>
    ProfileClaimDenied = 9,
    /// <summary>Sent to every member of a Family Group when someone
    /// attaches a story to it.</summary>
    FamilyGroupPostAdded = 10,
    /// <summary>Sent to a user who's just been added to a Family Group
    /// (and to existing members so they see the new face).</summary>
    FamilyGroupMemberJoined = 11,
    /// <summary>Sent to a user when their role changes in a Family Group
    /// — promoted to co-admin, demoted, or removed by an admin.</summary>
    FamilyGroupRoleChanged = 12
}

public class Notification
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    public NotificationType Type { get; set; }

    [Required, MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    public string? ActorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [ForeignKey(nameof(ActorUserId))]
    public ApplicationUser? Actor { get; set; }
}
