namespace MyStoryTold.Models;

public class CommentTranslation
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string? DetectedFromLanguage { get; set; }
    public string BodyTranslated { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
