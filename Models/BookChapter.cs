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

    /// <summary>Calendar year this chapter sits under in the TOC. The
    /// book renders chapters within their year section. Stories
    /// themselves keep their own EventYear — a chapter from 1989 can
    /// hold a story dated 1988 (e.g. "The big year" with prologue
    /// material). Cross-year membership is allowed; the chapter just
    /// renders in the year you gave it.</summary>
    public int Year { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>Order within the parent (or within the year if no
    /// parent). Lower = earlier. Ties broken by Id.</summary>
    public int SortOrder { get; set; }

    /// <summary>Parent chapter for nesting. Null = top-level chapter.
    /// One level deep is the sweet spot — book readers don't
    /// realistically use deeper hierarchies.</summary>
    public int? ParentChapterId { get; set; }

    [ForeignKey(nameof(ParentChapterId))]
    public BookChapter? ParentChapter { get; set; }

    public List<BookChapter> SubChapters { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
