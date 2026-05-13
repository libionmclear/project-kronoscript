using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// A photo uploaded directly to a Family Group's shared archive (not
/// attached to a single story). Each row tracks the uploader, the
/// stored URL, and the byte size so the per-group quota check stays
/// fast — one SUM query against the FamilyGroupMedia table tells us
/// how full the archive is without scanning blob storage.
/// </summary>
public class FamilyGroupMedia
{
    public int Id { get; set; }

    public int FamilyGroupId { get; set; }
    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    [Required]
    public string UploaderUserId { get; set; } = "";
    [ForeignKey(nameof(UploaderUserId))]
    public ApplicationUser? Uploader { get; set; }

    [Required, MaxLength(500)]
    public string Url { get; set; } = "";

    [MaxLength(120)]
    public string? ContentType { get; set; }

    /// <summary>Size on disk in bytes. Summed across the group to enforce
    /// the per-group quota. Stored at upload time; updates don't change it.</summary>
    public long ByteSize { get; set; }

    [MaxLength(500)]
    public string? Caption { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
