using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace MyStoryTold.Helpers;

public static class BodyRenderer
{
    // Matches [image: /uploads/filename.ext] or [image: https://...]
    private static readonly Regex ImagePattern =
        new Regex(@"\[image:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Renders a post/comment body, replacing [image: url] markers with
    /// 16:9 post-media-wrap img blocks and HTML-encoding the surrounding text.
    /// </summary>
    /// <summary>
    /// Returns a plain-text preview of the body (images stripped, truncated to maxChars).
    /// </summary>
    public static string TextPreview(string? body, int maxChars = 200)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var text = ImagePattern.Replace(body, string.Empty).Trim();
        if (text.Length > maxChars) text = text[..maxChars] + "…";
        return text;
    }

    public static IHtmlContent RenderBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return HtmlString.Empty;

        var html = ImagePattern.Replace(
            System.Web.HttpUtility.HtmlEncode(body),
            m =>
            {
                var url = m.Groups[1].Value.Trim();
                // Basic allow-list: only relative /uploads/ paths or https
                if (!url.StartsWith("/uploads/") && !url.StartsWith("https://"))
                    return m.Value; // leave unknown references as-is
                return $"<div class=\"post-media-wrap my-2\"><img src=\"{System.Web.HttpUtility.HtmlAttributeEncode(url)}\" alt=\"\" /></div>";
            });

        // Preserve line breaks
        html = html.Replace("\r\n", "\n").Replace("\n", "<br />");

        return new HtmlString(html);
    }
}
