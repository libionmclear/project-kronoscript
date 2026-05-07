using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum ReportTargetType
{
    Post = 0,
    Comment = 1,
    User = 2
}

public enum ReportStatus
{
    Pending = 0,
    Dismissed = 1,
    Actioned = 2
}

/// <summary>A user-flagged piece of content or user account, queued for admin review.</summary>
public class Report
{
    public int Id { get; set; }

    [Required]
    public string ReporterUserId { get; set; } = null!;

    public ReportTargetType TargetType { get; set; }

    /// <summary>For Post/Comment this is the integer Id as string; for User it's the AspNetUsers Id.</summary>
    [Required, MaxLength(64)]
    public string TargetId { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandledAt { get; set; }
    public string? HandledByUserId { get; set; }

    [ForeignKey(nameof(ReporterUserId))]
    public ApplicationUser? Reporter { get; set; }
}
