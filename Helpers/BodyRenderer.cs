using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace MyStoryTold.Helpers;

public static class BodyRenderer
{
    // Matches [image: /uploads/filename.ext] or [image: https://...]
    private static readonly Regex ImagePattern =
        new Regex(@"\[image:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Returns a plain-text preview of the body (images stripped, truncated to maxChars).</summary>
    public static string TextPreview(string? body, int maxChars = 200)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var text = ImagePattern.Replace(body, string.Empty).Trim();
        if (text.Length > maxChars) text = text[..maxChars] + "…";
        return text;
    }

    // Block-level tags whose open/close boundaries should become line breaks.
    private static readonly Regex BrTag = new(@"<\s*br\s*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlockClose = new(@"</\s*(div|p|li|ul|ol|h[1-6]|tr|blockquote|pre|section|article)\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlockOpen = new(@"<\s*(div|p|li|ul|ol|h[1-6]|tr|blockquote|pre|section|article)\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MultiNewline = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>
    /// Strips HTML markup from a body before persisting it. The contenteditable
    /// editor in Create wraps new lines in &lt;div&gt;, pasted content can carry
    /// &lt;span&gt;/&lt;p&gt;/&lt;b&gt;, etc.; RenderBody HTML-encodes on display
    /// so anything left here surfaces as literal markup in the post. Block tags
    /// and &lt;br&gt; become newlines so RenderBody's &lt;br /&gt; conversion
    /// still preserves the author's line breaks. [image: url] markers pass
    /// through untouched.
    /// </summary>
    public static string Sanitize(string? body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var s = body.Replace("\r\n", "\n").Replace('\r', '\n');
        s = BrTag.Replace(s, "\n");
        s = BlockClose.Replace(s, "\n");
        s = BlockOpen.Replace(s, string.Empty);
        s = AnyTag.Replace(s, string.Empty);
        s = System.Net.WebUtility.HtmlDecode(s);
        s = MultiNewline.Replace(s, "\n\n");
        return s.Trim();
    }

    /// <summary>
    /// Renders a post/comment body, replacing [image: url] markers with
    /// 16:9 post-media-wrap img blocks and HTML-encoding the surrounding text.
    /// </summary>
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
