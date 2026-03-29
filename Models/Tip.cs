using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

public enum TipType { New, Tip, Info, Warning }

public class Tip
{
    public int Id { get; set; }

    public TipType Type { get; set; } = TipType.Tip;

    [Required, MaxLength(500)]
    public string Text { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
