using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public class CommentLike
{
    public int Id { get; set; }

    public int CommentId { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CommentId))]
    public Comment Comment { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;
}
