using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(50)]
    public string? DisplayName { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; } // "Male" or "Female"

    [MaxLength(200)]
    public string? BirthPlace { get; set; }

    [MaxLength(200)]
    public string? CurrentLocation { get; set; }

    [MaxLength(500)]
    public string? ProfilePhotoUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastActivityAt { get; set; }

    // Navigation
    public ICollection<FriendConnection> SentFriendRequests { get; set; } = new List<FriendConnection>();
    public ICollection<FriendConnection> ReceivedFriendRequests { get; set; } = new List<FriendConnection>();
    public ICollection<RelativeConnection> RelativeConnectionsA { get; set; } = new List<RelativeConnection>();
    public ICollection<RelativeConnection> RelativeConnectionsB { get; set; } = new List<RelativeConnection>();
    public ICollection<LifeEventPost> Posts { get; set; } = new List<LifeEventPost>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<PostLike> Likes { get; set; } = new List<PostLike>();
}
