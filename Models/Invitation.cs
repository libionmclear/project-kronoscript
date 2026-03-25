using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public class Invitation
{
    public int Id { get; set; }

    [Required]
    public string Token { get; set; } = null!;

    [Required]
    public string InviterUserId { get; set; } = null!;

    public string? InviteeEmail { get; set; }

    [Required]
    public string Message { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool Used { get; set; }

    [ForeignKey(nameof(InviterUserId))]
    public ApplicationUser Inviter { get; set; } = null!;
}
