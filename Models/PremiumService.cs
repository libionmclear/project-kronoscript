namespace MyStoryTold.Models;

/// <summary>
/// Ad-hoc paid services offered alongside the subscription. These are
/// one-off purchases (a hardcover print run, an editing pass, an audio
/// book) delivered by partner services or Kronoscript staff — NOT part
/// of the recurring Premium subscription. The subscription unlocks
/// software features (<see cref="PremiumFeature"/>); these services
/// produce a tangible artefact.
/// </summary>
public class PremiumServiceInfo
{
    /// <summary>Stable slug used in URLs (e.g. /PremiumServices/Details/hardcover-printing).</summary>
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>One-line teaser shown on the index card.</summary>
    public string ShortDescription { get; init; } = "";
    /// <summary>Multi-paragraph body for the Details page.</summary>
    public string LongDescription { get; init; } = "";
    public string Category { get; init; } = "";
    public string IconEmoji { get; init; } = "✨";
    /// <summary>"Starting at" copy for the Details page (e.g. "from $89").
    /// Leave blank until pricing is firm; the view falls back to
    /// "Quote on request" when null/empty.</summary>
    public string? StartingPrice { get; init; }
}
