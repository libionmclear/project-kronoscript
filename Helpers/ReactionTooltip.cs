using MyStoryTold.Models;

namespace MyStoryTold.Helpers;

/// <summary>
/// Builds the small "❤️ Alice, Bob &lt;br&gt; 👍 Carol" HTML used as a Bootstrap
/// tooltip when hovering a post's reaction button. Names are HTML-encoded;
/// the tooltip is rendered via data-bs-html=true so the &lt;br&gt; works.
/// Returns null when no likes exist (so the widget can skip the tooltip).
/// </summary>
public static class ReactionTooltip
{
    private static readonly Dictionary<int, string> Emoji = new()
    {
        { 0, "❤️" },
        { 1, "👍" },
        { 2, "⭐" },
        { 3, "✓" },
        { 4, "😢" }
    };

    public static string? Build(IEnumerable<PostLike>? likes)
    {
        if (likes == null) return null;
        var groups = likes
            .GroupBy(l => (int)l.ReactionType)
            .OrderBy(g => g.Key)
            .ToList();
        if (groups.Count == 0) return null;

        var lines = new List<string>(groups.Count);
        foreach (var g in groups)
        {
            var emoji = Emoji.TryGetValue(g.Key, out var e) ? e : "•";
            var names = string.Join(", ", g.Select(l =>
                System.Web.HttpUtility.HtmlEncode(l.User?.DisplayName ?? l.User?.UserName ?? "Someone")));
            lines.Add($"{emoji} {names}");
        }
        return string.Join("<br>", lines);
    }
}
