using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public class MediaComment
{
    public int Id { get; set; }

    public int PostMediaId { get; set; }

    [Required]
    public string AuthorUserId { get; set; } = null!;

    [Required, MaxLength(1000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PostMediaId))]
    public PostMedia? Media { get; set; }

    [ForeignKey(nameof(AuthorUserId))]
    public ApplicationUser? Author { get; set; }
}
