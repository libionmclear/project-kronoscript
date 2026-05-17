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

    /// <summary>Book-mode override of <see cref="Body"/>. When set, the
    /// Book view renders this instead of Body — the user has tuned the
    /// prose for the bound-book context without disturbing the public
    /// post. When null, the book falls back to Body. Cleared when the
    /// user picks "also update the post" on save.</summary>
    public string? BookBody { get; set; }

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

    /// <summary>Owner-only mark: "I've reviewed this in the book view
    /// and it reads the way I want it to". Pure curation signal —
    /// doesn't affect visibility or anything else. Rendered as a tick
    /// badge on the story's page in Book mode; toggleable from there.</summary>
    public bool IsFinalised { get; set; } = false;

    /// <summary>When false, the post is hidden from the author's
    /// memoir Book view (and the Organize editor). Channel posts and
    /// posts written *as* a biographical user are filtered out by
    /// data shape; this flag is the manual override for anything the
    /// automatic filter doesn't catch (or that the author simply
    /// doesn't want in the book). Default true so existing posts
    /// keep appearing.</summary>
    public bool IncludeInBook { get; set; } = true;

    /// <summary>Premium-gated. When true, photos pasted/dropped into
    /// the editor stay embedded in the prose (the <c>&lt;img&gt;</c>
    /// tags survive Sanitize), and the post renders without the
    /// separate gallery strip. When false (default), photos are
    /// stripped from the body on save and routed to the gallery via
    /// PostMedia rows, which is the experience free users get.
    /// Existing inline posts keep rendering even if the author's
    /// subscription later lapses; only the *creation* of new inline
    /// stories is gated.</summary>
    public bool UseInlineImages { get; set; } = false;

    /// <summary>Optional book-mode chapter this post is grouped under.
    /// When set, the TOC renders the chapter title instead of the
    /// individual story title, and the body groups stories by chapter
    /// inside their year. Null = unassigned (appears under the year
    /// header on its own). Edited from the /Book/Organize page.</summary>
    public int? BookChapterId { get; set; }

    [ForeignKey(nameof(BookChapterId))]
    public BookChapter? BookChapter { get; set; }

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

    /// <summary>If this post is a reader's personal memory connected to a
    /// channel article (or any other source post), this points back to the
    /// source. The detail page of the source surfaces a list of connected
    /// memories; the memory itself shows an "inspired by" chip linking back.</summary>
    public int? MemoryOfPostId { get; set; }

    [ForeignKey(nameof(MemoryOfPostId))]
    public LifeEventPost? MemoryOf { get; set; }

    /// <summary>Admin "mute" — when set in the future, the post is hidden
    /// from feeds for everyone until this timestamp passes. Direct links,
    /// comments, search, and the writer's own timeline still surface it.
    /// Used by channel admins to temporarily quiet a post that's
    /// dominating the feed (or that needs cooling-off time).</summary>
    public DateTime? MutedUntil { get; set; }

    /// <summary>Admin "republish" — bumps this post back to the top of
    /// feeds for one rotation. Feed sort uses Coalesce(RepublishedAt,
    /// CreatedAt) DESC so a republished post is treated as if it were
    /// freshly posted *for ordering purposes only*; the original CreatedAt
    /// stays intact for byline / archive purposes.</summary>
    public DateTime? RepublishedAt { get; set; }

    [MaxLength(500)]
    public string? MusicUrl { get; set; }

    // Tags (comma-separated user IDs tagged by the post owner)
    [MaxLength(2000)]
    public string? TaggedUserIds { get; set; }

    /// <summary>Comma-separated PersonProfile IDs tagged on this post —
    /// the "memory people" / NPC equivalent of TaggedUserIds. Profiles
    /// belong to the post owner; rendering links to /PersonProfiles/Details.
    /// Kept as a separate column from TaggedUserIds because the two
    /// reference different tables (AspNetUsers vs PersonProfiles) and
    /// joining them would be lossy.</summary>
    [MaxLength(2000)]
    public string? TaggedProfileIds { get; set; }

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
            // Pulls the active UI culture (set by UseRequestLocalization
            // from the language cookie) so dates honor the viewer's
            // language: Italian capitalises the month name and uses "ca."
            // as the estimated marker; English keeps the existing format.
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            var isItalian = culture.TwoLetterISOLanguageName == "it";
            var estMarker = isItalian ? "ca." : "est.";
            var est = EventDateIsEstimated ? $" ({estMarker})" : "";
            var absYear = Math.Abs(EventYear);
            var era = EventYear < 0 ? " BC" : "";

            string MonthName(int month)
            {
                var name = new DateTime(2000, month, 1).ToString("MMMM", culture);
                if (isItalian && name.Length > 0)
                {
                    name = char.ToUpper(name[0]) + name[1..];
                }
                return name;
            }

            if (EventMonth.HasValue && EventDay.HasValue)
            {
                return $"{MonthName(EventMonth.Value)} {EventDay.Value}, {absYear}{era}{est}";
            }
            if (EventMonth.HasValue)
            {
                return $"{MonthName(EventMonth.Value)} {absYear}{era}{est}";
            }
            return $"{absYear}{era}{est}";
        }
    }
}
