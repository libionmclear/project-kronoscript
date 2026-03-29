using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

public class Message
{
    public int Id { get; set; }

    public string SenderUserId { get; set; } = null!;
    public ApplicationUser Sender { get; set; } = null!;

    public string RecipientUserId { get; set; } = null!;
    public ApplicationUser Recipient { get; set; } = null!;

    [MaxLength(2000)]
    public string Body { get; set; } = null!;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
