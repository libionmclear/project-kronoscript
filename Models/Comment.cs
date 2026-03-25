using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public class Comment
{
    public int Id { get; set; }

    public int PostId { get; set; }

    [Required]
    public string AuthorUserId { get; set; } = null!;

    [Required]
    public string Body { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? MentionedUserIds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public int EventYear { get; set; }
    public int? EventMonth { get; set; }
    public int? EventDay { get; set; }
    public bool EventDateIsEstimated { get; set; }

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;

    [ForeignKey(nameof(AuthorUserId))]
    public ApplicationUser Author { get; set; } = null!;

    [NotMapped]
    public string EventDateDisplay
    {
        get
        {
            var est = EventDateIsEstimated ? " (est.)" : "";
            if (EventMonth.HasValue && EventDay.HasValue)
                return $"{new DateTime(EventYear, EventMonth.Value, EventDay.Value):MMMM d, yyyy}{est}";
            if (EventMonth.HasValue)
                return $"{new DateTime(EventYear, EventMonth.Value, 1):MMMM yyyy}{est}";
            return $"{EventYear}{est}";
        }
    }
}
