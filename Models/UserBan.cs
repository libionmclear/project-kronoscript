using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

public enum BanType { Temporary, Permanent }

public class UserBan
{
    public int Id { get; set; }

    // Nullable — set to null when the user account is deleted
    public string? UserId { get; set; }

    [Required, MaxLength(256)]
    public string BannedEmail { get; set; } = null!;

    public BanType BanType { get; set; }

    public DateTime BannedAt { get; set; } = DateTime.UtcNow;

    // Null = permanent
    public DateTime? BanExpiry { get; set; }

    public string? BannedByUserId { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}
