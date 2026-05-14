using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// A user-authored "chapter" inside their book. Lives entirely within
/// a single calendar year — a year can have any number of chapters,
/// each carrying a free-text title (e.g. 1989 → "The big year",
/// "Coming home"). LifeEventPosts opt in via
/// <see cref="LifeEventPost.BookChapterId"/>; unassigned posts render
/// under the year header without a sub-title.
///
/// Naming note: unrelated to <see cref="LifeChapter"/>, which is about
/// people-context contexts (a job, a school, etc.). Book chapters are
/// editorial groupings inside the memoir.
/// </summary>
public class BookChapter
{
    public int Id { get; set; }

    public string OwnerUserId { get; set; } = null!;

    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? Owner { get; set; }

    /// <summary>Calendar year this chapter belongs to. Constraining
    /// a chapter to a single year keeps the TOC navigable — multi-year
    /// chapters would require an order-of-events tiebreak the book
    /// doesn't have yet.</summary>
    public int Year { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>Order within the year. Lower = earlier in the book.
    /// Ties broken by Id (creation order).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
