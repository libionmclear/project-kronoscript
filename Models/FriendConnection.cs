using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum FriendConnectionStatus
{
    Pending,
    Accepted,
    Blocked
}

public enum FriendTier
{
    Acquaintance,
    Friend,
    Family
}

public class FriendConnection
{
    public int Id { get; set; }

    [Required]
    public string RequesterUserId { get; set; } = null!;

    [Required]
    public string AddresseeUserId { get; set; } = null!;

    public FriendConnectionStatus Status { get; set; } = FriendConnectionStatus.Pending;
    public FriendTier Tier { get; set; } = FriendTier.Acquaintance;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(RequesterUserId))]
    public ApplicationUser Requester { get; set; } = null!;

    [ForeignKey(nameof(AddresseeUserId))]
    public ApplicationUser Addressee { get; set; } = null!;
}
