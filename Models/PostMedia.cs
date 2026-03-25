using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum MediaType
{
    Image,
    Video
}

public class PostMedia
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public MediaType MediaType { get; set; }

    [Required]
    [MaxLength(500)]
    public string Url { get; set; } = null!;

    [MaxLength(300)]
    public string? Caption { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;
}
