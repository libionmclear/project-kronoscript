using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum NotificationType
{
    Comment = 0,
    Mention = 1,
    Reply = 2,
    FriendRequest = 3,
    FriendAccepted = 4
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
