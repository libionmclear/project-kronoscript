using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum PostVisibility
{
    Public,
    Acquaintances,
    Friends,
    Family,
    Private
}

public class LifeEventPost
{
    public int Id { get; set; }

    [Required]
    public string OwnerUserId { get; set; } = null!;

    [Required]
    public string Body { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public int EventYear { get; set; }
    public int? EventMonth { get; set; }
    public int? EventDay { get; set; }
    public bool EventDateIsEstimated { get; set; }

    public DateTime? LastEditedAt { get; set; }
    public int CurrentVersionNumber { get; set; } = 1;

    // Story ordering (used by Family tier)
    public int? StoryOrder { get; set; }
    public string? LastReorderedByUserId { get; set; }
    public DateTime? LastReorderedAt { get; set; }

    public PostVisibility Visibility { get; set; } = PostVisibility.Friends;

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? MusicUrl { get; set; }

    // Tags (comma-separated user IDs tagged by the post owner)
    [MaxLength(2000)]
    public string? TaggedUserIds { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser Owner { get; set; } = null!;

    public ICollection<PostVersion> Versions { get; set; } = new List<PostVersion>();
    public ICollection<PostMedia> Media { get; set; } = new List<PostMedia>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<PostLike> Likes { get; set; } = new List<PostLike>();

    [NotMapped]
    public string EventDateDisplay
    {
        get
        {
            var est = EventDateIsEstimated ? " (est.)" : "";
            var absYear = Math.Abs(EventYear);
            var era = EventYear < 0 ? " BC" : "";

            if (EventMonth.HasValue && EventDay.HasValue)
            {
                var monthName = new DateTime(2000, EventMonth.Value, 1).ToString("MMMM");
                return $"{monthName} {EventDay.Value}, {absYear}{era}{est}";
            }
            if (EventMonth.HasValue)
            {
                var monthName = new DateTime(2000, EventMonth.Value, 1).ToString("MMMM");
                return $"{monthName} {absYear}{era}{est}";
            }
            return $"{absYear}{era}{est}";
        }
    }
}
