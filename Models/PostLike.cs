using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

public enum ReactionType
{
    Heart = 0,
    ThumbsUp = 1,
    Awesome = 2,
    IWasThere = 3
}

public class PostLike
{
    public int Id { get; set; }

    public int PostId { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    public ReactionType ReactionType { get; set; } = ReactionType.Heart;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PostId))]
    public LifeEventPost Post { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;
}
