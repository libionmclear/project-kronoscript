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

    /// <summary>How many cells of the 4-column layout grid the image spans
    /// (1–4). 4 grid columns = full article width (= 1 article column in
    /// book mode, or the full 2-column spread in newspaper mode).</summary>
    public int LayoutWidth { get; set; } = 1;

    /// <summary>How many rows of the 8-row layout grid the image spans
    /// (1–8). Sets the visible height of the figure.</summary>
    public int LayoutHeight { get; set; } = 1;

    /// <summary>Origin column on the 4×8 layout grid (0–3). -1 = unset
    /// (legacy posts that only have LayoutPosition derive col from that).</summary>
    public int LayoutCol { get; set; } = -1;

    /// <summary>Origin row on the 4×8 layout grid (0–7). -1 = unset.</summary>
    public int LayoutRow { get; set; } = -1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;
}
