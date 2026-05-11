using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace MyStoryTold.Helpers;

/// <summary>Reference to a person tagged in a post — used by the body
/// renderer to convert @DisplayName occurrences into clickable links.</summary>
public class MentionRef
{
    public string DisplayName { get; init; } = "";
    public string Href { get; init; } = "";
    /// <summary>True for People Profile tags — prefixes the linked name with the 🕊 marker.</summary>
    public bool IsProfile { get; init; }
}

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
    // Any whitespace at the start of a line — \s covers Unicode whitespace
    // (NBSP, em/en-space, ideographic space, etc.); the explicit zero-width
    // chars catch the rest (Word and Docs sometimes drop U+200B in front of
    // indented paragraphs alongside the actual whitespace).
    private static readonly Regex LeadingIndent = new("^[\\s​‌‍﻿]+", RegexOptions.Compiled);

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
        // Strip leading whitespace from each line so paragraphs don't render
        // indented (pre-wrap preserves it otherwise). Word/Docs commonly drop
        // tabs, NBSPs, em-spaces, and zero-widths in front of paragraphs.
        var lines = s.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = LeadingIndent.Replace(lines[i], string.Empty);
        }
        s = string.Join("\n", lines);
        s = MultiNewline.Replace(s, "\n\n");
        return s.Trim();
    }

    /// <summary>
    /// Renders a post/comment body, replacing [image: url] markers with
    /// 16:9 post-media-wrap img blocks and HTML-encoding the surrounding text.
    /// Optionally rewrites @DisplayName occurrences into clickable links for
    /// each mention in the supplied list (members and people profiles alike).
    /// </summary>
    public static IHtmlContent RenderBody(string? body, IReadOnlyList<MentionRef>? mentions = null)
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

        html = ApplyMentions(html, mentions);

        // Preserve line breaks
        html = html.Replace("\r\n", "\n").Replace("\n", "<br />");

        return new HtmlString(html);
    }

    // Match @DisplayName in already-encoded HTML. We pre-encode the name when
    // building the per-mention regex so &amp; / &#39; etc. in display names
    // align with what's in the html string.
    private static string ApplyMentions(string encodedHtml, IReadOnlyList<MentionRef>? mentions)
    {
        if (mentions == null || mentions.Count == 0) return encodedHtml;
        // Longest names first so "Anna Maria" matches before "Anna".
        foreach (var m in mentions.OrderByDescending(x => x.DisplayName.Length))
        {
            if (string.IsNullOrWhiteSpace(m.DisplayName)) continue;
            var encodedName = System.Web.HttpUtility.HtmlEncode(m.DisplayName);
            // @Name preceded by start / whitespace / common punctuation, and
            // followed by end / whitespace / punctuation. No \b — names can
            // contain spaces and apostrophes which break word boundaries.
            var pattern = new Regex(
                @"(?<=^|[\s.,;:!?\(\[\""“‘])@" + Regex.Escape(encodedName) + @"(?=$|[\s.,;:!?\)\]\""”’])",
                RegexOptions.Compiled);
            var prefix = m.IsProfile ? "🕊 " : "";
            var hrefAttr = System.Web.HttpUtility.HtmlAttributeEncode(m.Href);
            encodedHtml = pattern.Replace(encodedHtml,
                $"<a class=\"post-mention\" href=\"{hrefAttr}\">@{prefix}{encodedName}</a>");
        }
        return encodedHtml;
    }

    /// <summary>
    /// Splits a body into its paragraphs (split on blank lines) and returns
    /// each rendered as a self-contained HTML fragment — line breaks within a
    /// paragraph become &lt;br /&gt;, [image: …] markers expand to media wraps
    /// just like RenderBody. Used by article layouts so that figures can be
    /// injected *between* paragraphs (instead of before the prose), letting
    /// text flow above and below mid-band images.
    /// </summary>
    public static List<IHtmlContent> RenderBodyParagraphs(string? body, IReadOnlyList<MentionRef>? mentions = null)
    {
        var result = new List<IHtmlContent>();
        if (string.IsNullOrEmpty(body)) return result;

        var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n');
        // Sanitize already collapses 3+ newlines to "\n\n"; split on that.
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.None);
        foreach (var raw in paragraphs)
        {
            var trimmed = raw.Trim('\n');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var html = ImagePattern.Replace(
                System.Web.HttpUtility.HtmlEncode(trimmed),
                m =>
                {
                    var url = m.Groups[1].Value.Trim();
                    if (!url.StartsWith("/uploads/") && !url.StartsWith("https://"))
                        return m.Value;
                    return $"<div class=\"post-media-wrap my-2\"><img src=\"{System.Web.HttpUtility.HtmlAttributeEncode(url)}\" alt=\"\" /></div>";
                });
            html = ApplyMentions(html, mentions);
            html = html.Replace("\n", "<br />");
            result.Add(new HtmlString(html));
        }
        return result;
    }
}
