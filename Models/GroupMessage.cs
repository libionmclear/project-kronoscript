using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStoryTold.Models;

/// <summary>
/// A single message in a <see cref="FamilyGroup"/>'s chat. Cascades on
/// group delete (the chat history goes away with the group) and on
/// sender delete (Cascade — keep the model simple; private DMs are in
/// the Messages table and use a different policy).
/// </summary>
public class GroupMessage
{
    public int Id { get; set; }

    public int FamilyGroupId { get; set; }
    [ForeignKey(nameof(FamilyGroupId))]
    public FamilyGroup? FamilyGroup { get; set; }

    [Required]
    public string SenderUserId { get; set; } = "";
    [ForeignKey(nameof(SenderUserId))]
    public ApplicationUser? Sender { get; set; }

    [Required, MaxLength(2000)]
    public string Body { get; set; } = "";

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
