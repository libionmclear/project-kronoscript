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

    /// <summary>Display order within the post (lower first; ties broken by Id).
    /// First image is used as the home-feed cover thumbnail.</summary>
    public int SortOrder { get; set; }

    /// <summary>Horizontal focal point as a percentage (0-100) — used as the
    /// CSS object-position x value when the image is rendered inside a
    /// cover-crop tile. Default 50 = center. The writer can click anywhere
    /// on the thumbnail in the Edit page to set this.</summary>
    public int FocusX { get; set; } = 50;

    /// <summary>Vertical focal point (0-100). Default 50 = center.</summary>
    public int FocusY { get; set; } = 50;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;
}
