using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>Coarse-grained "type of context" the chapter belongs to.
/// Drives the default colour of the bubble on the Life Map and lets
/// the layout group similar chapters into the same band.</summary>
public enum LifeChapterCategory
{
    Work = 0,
    School = 1,
    University = 2,
    Religious = 3,
    Hobby = 4,
    Neighborhood = 5,
    Online = 6,
    Travel = 7,
    Family = 8,
    Other = 9
}

/// <summary>A "context" the user lived through — a job, school year,
/// church congregation, summer camp, online community. Has a year
/// range and a set of <see cref="PersonProfile"/> members. Plotted as
/// a bubble on the Life Map: width = year span, avatars of members
/// tile inside, category drives the colour band.
///
/// Owned by the creator and visible only to them — this is a personal
/// recollection device, not a shareable graph.</summary>
public class LifeChapter
{
    public int Id { get; set; }

    public string OwnerUserId { get; set; } = null!;

    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? Owner { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = null!;

    public LifeChapterCategory Category { get; set; } = LifeChapterCategory.Other;

    /// <summary>Year the chapter started (you took the job, started
    /// school, joined the church). Required so the bubble has an
    /// anchor on the timeline.</summary>
    public int StartYear { get; set; }

    /// <summary>Year the chapter ended. Null = still active; bubble
    /// extends to "today" with a dashed right edge on the Map.</summary>
    public int? EndYear { get; set; }

    /// <summary>Optional override of the category's default colour
    /// (any CSS colour). Lets the user distinguish two work chapters
    /// at the same employer-tier, etc.</summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>Short description shown in the chapter's expanded
    /// panel — "engineering team at Acme, downtown office".</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<LifeChapterMember> Members { get; set; } = new();
}

/// <summary>Join row: which PersonProfile belongs to which LifeChapter.
/// A profile can belong to many chapters (childhood friend who later
/// became a coworker) and a chapter has many profiles.</summary>
public class LifeChapterMember
{
    public int Id { get; set; }

    public int LifeChapterId { get; set; }

    [ForeignKey(nameof(LifeChapterId))]
    public LifeChapter? LifeChapter { get; set; }

    public int PersonProfileId { get; set; }

    [ForeignKey(nameof(PersonProfileId))]
    public PersonProfile? PersonProfile { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
