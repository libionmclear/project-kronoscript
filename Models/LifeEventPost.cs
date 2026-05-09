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

public enum PostLayoutStyle
{
    /// <summary>Default — owner/avatar header, body, media grid below.</summary>
    Standard = 0,
    /// <summary>Newspaper article layout: serif headline, two-column body,
    /// drop-cap, photos floated by their LayoutPosition.</summary>
    Newspaper = 1,
    /// <summary>Book chapter layout: italic Fraunces title, narrower single
    /// column with luxurious leading, gold drop-cap, photos floated by
    /// their LayoutPosition.</summary>
    Book = 2
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

    /// <summary>Visual layout used when rendering this post on the Detail
    /// page. Standard = the regular feed-style card. Newspaper / Book turn
    /// the post into an article with a serif headline, multi-column or
    /// chapter-style body, and image floats driven by PostMedia.LayoutPosition.</summary>
    public PostLayoutStyle LayoutStyle { get; set; } = PostLayoutStyle.Standard;

    /// <summary>True while the owner is still working on the post.
    /// Drafts are hidden from feeds and other people's timelines.</summary>
    public bool IsDraft { get; set; } = false;

    /// <summary>Soft-delete timestamp. When non-null the post sits in the owner's
    /// "Deleted Stories" archive — a global query filter hides it from every
    /// normal query; the archive view opts in via IgnoreQueryFilters().</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Channel this post belongs to (e.g. "History"). Null for ordinary
    /// personal posts. Only the channel's assigned admin (or app admins) can
    /// publish into a channel; the home feed renders channel posts with a
    /// yellow accent and a channel badge.</summary>
    public int? ChannelId { get; set; }

    [ForeignKey(nameof(ChannelId))]
    public Channel? Channel { get; set; }

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
