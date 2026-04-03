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
