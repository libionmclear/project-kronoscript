using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// Two-way invisibility marker. When BlockerUserId blocks BlockedUserId:
/// - BlockerUserId stops seeing BlockedUserId's posts, profile, search results.
/// - BlockedUserId can no longer send friend requests, messages, or comments
///   to BlockerUserId. Existing connections are not severed automatically.
/// </summary>
public class UserBlock
{
    public int Id { get; set; }

    [Required]
    public string BlockerUserId { get; set; } = null!;

    [Required]
    public string BlockedUserId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(BlockerUserId))]
    public ApplicationUser? Blocker { get; set; }

    [ForeignKey(nameof(BlockedUserId))]
    public ApplicationUser? Blocked { get; set; }
}
