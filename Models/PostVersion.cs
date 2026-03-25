using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public class PostVersion
{
    public int Id { get; set; }

    public int PostId { get; set; }
    public int VersionNumber { get; set; }

    [Required]
    public string BodySnapshot { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? TitleSnapshot { get; set; }

    public DateTime EditedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public string EditedByUserId { get; set; } = null!;

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;

    [ForeignKey(nameof(EditedByUserId))]
    public ApplicationUser EditedBy { get; set; } = null!;
}
