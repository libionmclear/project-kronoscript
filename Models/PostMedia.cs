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

    /// <summary>Where this image floats inside a newspaper-style channel
    /// post or book-style biographical post. One of:
    /// "top-left", "top", "top-right", "left", "center", "right",
    /// "bottom-left", "bottom", "bottom-right". Null = inline / no special
    /// placement (used by ordinary personal posts).</summary>
    [MaxLength(20)]
    public string? LayoutPosition { get; set; }

    /// <summary>How many columns of the 3×3 layout grid the image spans
    /// from <see cref="LayoutPosition"/>. 1 = single cell width (default),
    /// 2 = wider float that takes two cells horizontally.</summary>
    public int LayoutWidth { get; set; } = 1;

    /// <summary>How many rows the image spans (1 = single row, 2 = taller).
    /// LayoutWidth=2 + LayoutHeight=2 = a 2×2 hero image.</summary>
    public int LayoutHeight { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;
}
