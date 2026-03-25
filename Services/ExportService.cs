using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;

namespace MyStoryTold.Services;

public class ExportService : IExportService
{
    private readonly ApplicationDbContext _db;

    public ExportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<byte[]> ExportPostsAsDocxAsync(string userId)
    {
        var posts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId)
            .OrderBy(p => p.EventYear)
            .ThenBy(p => p.EventMonth)
            .ThenBy(p => p.EventDay)
            .ToListAsync();

        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Title
            var titlePara = body.AppendChild(new Paragraph());
            var titleRun = titlePara.AppendChild(new Run());
            titleRun.AppendChild(new RunProperties(new Bold(), new FontSize { Val = "48" }));
            titleRun.AppendChild(new Text("My Story Told"));

            // Subtitle
            var subPara = body.AppendChild(new Paragraph());
            var subRun = subPara.AppendChild(new Run());
            subRun.AppendChild(new RunProperties(new Italic(), new FontSize { Val = "24" }));
            subRun.AppendChild(new Text($"Exported on {DateTime.UtcNow:MMMM d, yyyy}"));

            body.AppendChild(new Paragraph()); // blank line

            foreach (var post in posts)
            {
                // Event date header
                var datePara = body.AppendChild(new Paragraph());
                var dateRun = datePara.AppendChild(new Run());
                dateRun.AppendChild(new RunProperties(new Bold(), new FontSize { Val = "28" }));
                dateRun.AppendChild(new Text(post.EventDateDisplay));

                // Title if present
                if (!string.IsNullOrWhiteSpace(post.Title))
                {
                    var tPara = body.AppendChild(new Paragraph());
                    var tRun = tPara.AppendChild(new Run());
                    tRun.AppendChild(new RunProperties(new Bold()));
                    tRun.AppendChild(new Text(post.Title));
                }

                // Body
                var bodyLines = post.Body.Split('\n');
                foreach (var line in bodyLines)
                {
                    var para = body.AppendChild(new Paragraph());
                    var run = para.AppendChild(new Run());
                    run.AppendChild(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
                }

                // Separator
                body.AppendChild(new Paragraph());
                var sepPara = body.AppendChild(new Paragraph());
                var sepRun = sepPara.AppendChild(new Run());
                sepRun.AppendChild(new Text("───────────────────────────────"));
                body.AppendChild(new Paragraph());
            }
        }

        return stream.ToArray();
    }

    public async Task<byte[]> ExportPostsAsTxtAsync(string userId)
    {
        var posts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId)
            .OrderBy(p => p.EventYear)
            .ThenBy(p => p.EventMonth)
            .ThenBy(p => p.EventDay)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("MY STORY TOLD");
        sb.AppendLine($"Exported on {DateTime.UtcNow:MMMM d, yyyy}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        foreach (var post in posts)
        {
            sb.AppendLine($"[{post.EventDateDisplay}]");
            if (!string.IsNullOrWhiteSpace(post.Title))
                sb.AppendLine(post.Title);
            sb.AppendLine();
            sb.AppendLine(post.Body);
            sb.AppendLine();
            sb.AppendLine(new string('-', 50));
            sb.AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
