using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum WorkingIndexMood
{
    Unset   = 0,
    Great   = 1,
    Good    = 2,
    Neutral = 3,
    Tough   = 4,
    Heavy   = 5
}

/// <summary>
/// Private per-user life grid: one row per year. Used as scaffolding the
/// owner can later mine for memory prompts. Not shared with anyone.
/// </summary>
public class WorkingIndexEntry
{
    public int Id { get; set; }

    [Required]
    public string OwnerUserId { get; set; } = null!;

    public int Year { get; set; }

    [MaxLength(500)] public string? MainEvent    { get; set; }
    [MaxLength(300)] public string? Residence    { get; set; }
    [MaxLength(300)] public string? SchoolJob    { get; set; }
    [MaxLength(300)] public string? Relationship { get; set; }
    [MaxLength(300)] public string? Family       { get; set; }
    [MaxLength(300)] public string? Vacation     { get; set; }
    [MaxLength(500)] public string? Other        { get; set; }
    [MaxLength(2000)] public string? Notes       { get; set; }

    public WorkingIndexMood Mood { get; set; } = WorkingIndexMood.Unset;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? Owner { get; set; }
}
