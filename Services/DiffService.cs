using DiffPlex;
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
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        var sb = new System.Text.StringBuilder();
        foreach (var line in diff.Lines)
        {
            var encoded = System.Web.HttpUtility.HtmlEncode(line.Text);
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    sb.AppendLine($"<span class=\"diff-added\">{encoded}</span>");
                    break;
                case ChangeType.Deleted:
                    sb.AppendLine($"<span class=\"diff-removed\">{encoded}</span>");
                    break;
                case ChangeType.Modified:
                    sb.AppendLine($"<span class=\"diff-changed\">{encoded}</span>");
                    break;
                default:
                    sb.AppendLine(encoded);
                    break;
            }
        }
        return sb.ToString();
    }
}
