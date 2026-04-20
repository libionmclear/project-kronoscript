using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

/// <summary>
/// One memory prompt rotated daily on the sidebar / shown when admin picks.
/// Same shape as QuillMessage (text + sort + active flag).
/// </summary>
public class MemoryPrompt
{
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
