using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MyStoryTold.Helpers;

namespace MyStoryTold.Services;

public interface IDiffService
{
    string ComputeDiffHtml(string oldText, string newText);
}

public class DiffService : IDiffService
{
    private static readonly Regex ImagePattern =
        new Regex(@"\[image:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string ComputeDiffHtml(string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText) || oldText == newText)
            return RenderWithImages(System.Web.HttpUtility.HtmlEncode(newText));

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText,
            ignoreWhitespace: false, ignoreCase: false, new WordChunker());

        var sb = new System.Text.StringBuilder();
        foreach (var piece in diff.Lines)
        {
            var encoded = System.Web.HttpUtility.HtmlEncode(piece.Text);
            switch (piece.Type)
            {
                case ChangeType.Inserted:
                    sb.Append($"<span class=\"diff-added\">{encoded}</span>");
                    break;
                case ChangeType.Deleted:
                    sb.Append($"<span class=\"diff-removed\">{encoded}</span>");
                    break;
                default:
                    sb.Append(encoded);
                    break;
            }
        }
        return RenderWithImages(sb.ToString());
    }

    // Replace [image: url] markers that survived encoding (no special chars in paths)
    private static string RenderWithImages(string html)
    {
        return ImagePattern.Replace(html, m =>
        {
            var url = m.Groups[1].Value.Trim();
            if (!url.StartsWith("/uploads/") && !url.StartsWith("https://"))
                return m.Value;
            return $"<div class=\"post-media-wrap my-2\"><img src=\"{System.Web.HttpUtility.HtmlAttributeEncode(url)}\" alt=\"\" /></div>";
        });
    }
}
