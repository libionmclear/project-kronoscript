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

    /// <summary>Returns a plain-text preview of the body (all images
    /// stripped — both [image: url] markers and inline-mode &lt;img&gt;
    /// tags — truncated to maxChars).</summary>
    public static string TextPreview(string? body, int maxChars = 200)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var text = ImagePattern.Replace(body, string.Empty);
        // Inline-mode posts may have <img> tags in the body; strip them
        // too so the preview is pure prose.
        text = Regex.Replace(text, @"<img\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        text = text.Trim();
        if (text.Length > maxChars) text = text[..maxChars] + "…";
        return text;
    }

    // Block-level tags whose open/close boundaries should become line breaks.
    private static readonly Regex BrTag = new(@"<\s*br\s*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlockClose = new(@"</\s*(div|p|li|ul|ol|h[1-6]|tr|blockquote|pre|section|article)\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlockOpen = new(@"<\s*(div|p|li|ul|ol|h[1-6]|tr|blockquote|pre|section|article)\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MultiNewline = new(@"\n{3,}", RegexOptions.Compiled);

    // Inline-image markup (premium-gated). We pre-extract <img> tags
    // before the generic tag strip in Sanitize, validate the src against
    // a host allow-list, and re-emit a minimal safe tag carrying only
    // src + class (for float helpers). Same regex is reused on the
    // render side to split text from images.
    private static readonly Regex ImgTag = new(
        @"<img\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SrcAttr = new(
        @"\bsrc\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClassAttr = new(
        @"\bclass\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // CSS classes the inline-image flow may carry. Anything else is
    // silently dropped — no inline styles, no event handlers, no exotic
    // class names. Keep this list tight.
    //
    // Position classes live on a 2×3 grid (top/bottom × left/center/right)
    // so premium writers can place a photo anywhere in the paragraph and
    // have text wrap around it. The legacy float helpers stay as aliases
    // for backwards compat with previously saved posts.
    private static readonly HashSet<string> AllowedImgClasses = new(StringComparer.Ordinal)
    {
        // Legacy (pre-2026-05-17)
        "img-float-left",
        "img-float-right",
        "img-block",
        // 2×3 position grid
        "img-pos-top-left",
        "img-pos-top-center",
        "img-pos-top-right",
        "img-pos-bottom-left",
        "img-pos-bottom-center",
        "img-pos-bottom-right"
    };

    private static bool IsAllowedImageSrc(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        // javascript: / data: dressed as something else — reject early.
        if (url.IndexOf("javascript:", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (url.IndexOf("data:",       StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (url.StartsWith("/", StringComparison.Ordinal)) return true; // relative path
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        // Trusted hosts: Azure blob storage (our case) + any wildcard the
        // ops team adds later via env. Failure mode is permissive within
        // https — the editor only ever supplies blob URLs anyway, so the
        // allow-list is belt-and-braces.
        try
        {
            var host = new Uri(url).Host;
            return host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".kronoscript.com",       StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".azurewebsites.net",     StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string FilterImgClasses(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var keep = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Where(c => AllowedImgClasses.Contains(c))
                      .Distinct();
        return string.Join(" ", keep);
    }
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
    public static string Sanitize(string? body, bool allowInlineImages = false)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var s = body.Replace("\r\n", "\n").Replace('\r', '\n');

        // Premium-only branch: pre-extract whitelisted <img> tags into
        // sentinel placeholders BEFORE the generic strip below, so they
        // survive. The img tags we re-emit carry only src and (if set)
        // a whitelisted float class — every other attribute is dropped.
        var imgPlaceholders = new List<string>();
        if (allowInlineImages)
        {
            s = ImgTag.Replace(s, m =>
            {
                var raw = m.Value;
                var srcMatch = SrcAttr.Match(raw);
                if (!srcMatch.Success) return string.Empty;
                var url = srcMatch.Groups[1].Value.Trim();
                if (!IsAllowedImageSrc(url)) return string.Empty;

                var classMatch = ClassAttr.Match(raw);
                var cls = classMatch.Success
                    ? FilterImgClasses(classMatch.Groups[1].Value)
                    : string.Empty;

                var safeUrl = System.Web.HttpUtility.HtmlAttributeEncode(url);
                var rebuilt = string.IsNullOrEmpty(cls)
                    ? $"<img src=\"{safeUrl}\" />"
                    : $"<img src=\"{safeUrl}\" class=\"{cls}\" />";

                imgPlaceholders.Add(rebuilt);
                return $"IMG{imgPlaceholders.Count - 1}";
            });
        }

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
        s = s.Trim();

        // Re-insert the inline images.
        for (int i = 0; i < imgPlaceholders.Count; i++)
        {
            s = s.Replace($"IMG{i}", imgPlaceholders[i]);
        }
        return s;
    }

    /// <summary>
    /// Renders a post/comment body, replacing [image: url] markers with
    /// 16:9 post-media-wrap img blocks and HTML-encoding the surrounding text.
    /// Optionally rewrites @DisplayName occurrences into clickable links for
    /// each mention in the supplied list (members and people profiles alike).
    /// </summary>
    public static IHtmlContent RenderBody(string? body, IReadOnlyList<MentionRef>? mentions = null, bool allowInlineImages = false)
    {
        if (string.IsNullOrEmpty(body))
            return HtmlString.Empty;

        // When inline-images is on, the body coming out of Sanitize is
        // already vetted markup (img tags carry only src + allowlisted
        // class). Pull them out before HTML-encoding the surrounding
        // text, then re-insert wrapped in a span so the float CSS hooks
        // have something to anchor to.
        var inlineImgs = new List<string>();
        var working = body;
        if (allowInlineImages)
        {
            working = ImgTag.Replace(working, m =>
            {
                inlineImgs.Add(m.Value);
                return $"INLINEIMG{inlineImgs.Count - 1}END";
            });
        }

        var html = ImagePattern.Replace(
            System.Web.HttpUtility.HtmlEncode(working),
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

        // Splice the trusted inline-image markup back in. Wrapping span
        // gives the float CSS a hook AND keeps the image from collapsing
        // out of an empty paragraph in the book view.
        for (int i = 0; i < inlineImgs.Count; i++)
        {
            // Emit the bare <img> — wrapping in a span was trapping the
            // float inside an inline-block parent so text never wrapped
            // in the published view (it worked in the editor because
            // the contenteditable surface doesn't add the wrapper).
            html = html.Replace($"INLINEIMG{i}END", inlineImgs[i]);
        }

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
    public static List<IHtmlContent> RenderBodyParagraphs(string? body, IReadOnlyList<MentionRef>? mentions = null, bool allowInlineImages = false)
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

            var working = trimmed;
            var inlineImgs = new List<string>();
            if (allowInlineImages)
            {
                working = ImgTag.Replace(working, m =>
                {
                    inlineImgs.Add(m.Value);
                    return $"INLINEIMG{inlineImgs.Count - 1}END";
                });
            }

            var html = ImagePattern.Replace(
                System.Web.HttpUtility.HtmlEncode(working),
                m =>
                {
                    var url = m.Groups[1].Value.Trim();
                    if (!url.StartsWith("/uploads/") && !url.StartsWith("https://"))
                        return m.Value;
                    return $"<div class=\"post-media-wrap my-2\"><img src=\"{System.Web.HttpUtility.HtmlAttributeEncode(url)}\" alt=\"\" /></div>";
                });
            html = ApplyMentions(html, mentions);
            html = html.Replace("\n", "<br />");

            for (int i = 0; i < inlineImgs.Count; i++)
            {
                html = html.Replace($"INLINEIMG{i}END",
                    $"<span class=\"post-inline-img\">{inlineImgs[i]}</span>");
            }

            result.Add(new HtmlString(html));
        }
        return result;
    }
}
