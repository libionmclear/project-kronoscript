using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace MyStoryTold.Services;

public interface IDiffService
{
    string ComputeDiffHtml(string oldText, string newText);
}

public class DiffService : IDiffService
{
    public string ComputeDiffHtml(string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText) || oldText == newText)
            return System.Web.HttpUtility.HtmlEncode(newText);

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
        return sb.ToString();
    }
}
