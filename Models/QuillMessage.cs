using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

/// <summary>
/// One line of the typewriter quill animation on the public landing page.
/// Admin can add/edit/reorder/delete via /Admin/QuillMessages.
/// </summary>
public class QuillMessage
{
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
