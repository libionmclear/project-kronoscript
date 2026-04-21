namespace MyStoryTold.Models;

public class PostTranslation
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string? DetectedFromLanguage { get; set; }
    public string? TitleTranslated { get; set; }
    public string BodyTranslated { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
