using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Helpers;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public class AzureTranslationService : ITranslationService
{
    private readonly HttpClient _http;
    private readonly ApplicationDbContext _db;
    private readonly string _endpoint;
    private readonly string _region;
    private readonly string _key;

    public AzureTranslationService(HttpClient http, ApplicationDbContext db, IConfiguration config)
    {
        _http = http;
        _db = db;
        _endpoint = (config["Azure:Translator:Endpoint"] ?? "https://api.cognitive.microsofttranslator.com").TrimEnd('/');
        _region = config["Azure:Translator:Region"] ?? "";
        _key = config["Azure:Translator:Key"] ?? "";
    }

    // Slot describes one piece of content whose translation we need.
    // Kind = "post-title" | "post-body" | "comment:{id}"
    private record Slot(string Kind, string Text);

    public async Task<PostTranslationResult> TranslatePostAsync(int postId, string targetLanguage, CancellationToken ct = default)
    {
        var target = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage.Trim();

        var post = await _db.LifeEventPosts.FindAsync(new object[] { postId }, ct);
        if (post == null) throw new InvalidOperationException("Post not found.");

        var comments = await _db.Comments
            .Where(c => c.PostId == postId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var postCache = await _db.PostTranslations
            .FirstOrDefaultAsync(t => t.PostId == postId && t.LanguageCode == target, ct);

        var commentIds = comments.Select(c => c.Id).ToList();
        var commentCaches = await _db.CommentTranslations
            .Where(t => commentIds.Contains(t.CommentId) && t.LanguageCode == target)
            .ToDictionaryAsync(t => t.CommentId, t => t, ct);

        // Figure out what's missing.
        var hasTitle = !string.IsNullOrWhiteSpace(post.Title);
        var bodyInput = BodyRenderer.TextPreview(post.Body, int.MaxValue);

        var slots = new List<Slot>();
        if (postCache == null)
        {
            if (hasTitle) slots.Add(new Slot("post-title", post.Title!));
            slots.Add(new Slot("post-body", bodyInput));
        }
        foreach (var c in comments)
        {
            if (!commentCaches.ContainsKey(c.Id) && !string.IsNullOrWhiteSpace(c.Body))
                slots.Add(new Slot("comment:" + c.Id, c.Body));
        }

        // Call Azure for missing slots, if any.
        List<(string Kind, string Translated, string? DetectedFrom)> fresh = new();
        if (slots.Count > 0)
        {
            if (string.IsNullOrEmpty(_key))
                throw new InvalidOperationException("Azure Translator key is not configured. Set 'Azure:Translator:Key' in user-secrets.");

            var payload = slots.Select(s => new { Text = s.Text }).Cast<object>().ToList();
            var url = $"{_endpoint}/translate?api-version=3.0&to={Uri.EscapeDataString(target)}";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Ocp-Apim-Subscription-Key", _key);
            if (!string.IsNullOrEmpty(_region))
                req.Headers.Add("Ocp-Apim-Subscription-Region", _region);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Translator API {(int)resp.StatusCode}: {err}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;

            for (int i = 0; i < slots.Count; i++)
            {
                var el = arr[i];
                var translated = el.GetProperty("translations")[0].GetProperty("text").GetString() ?? string.Empty;
                string? detected = null;
                if (el.TryGetProperty("detectedLanguage", out var dl))
                    detected = dl.GetProperty("language").GetString();
                fresh.Add((slots[i].Kind, translated, detected));
            }

            // Persist: group post slots into one row, each comment slot into its own row.
            string? newTitle = null;
            string? newBody = null;
            string? postFrom = null;
            foreach (var f in fresh)
            {
                if (f.Kind == "post-title") { newTitle = f.Translated; postFrom ??= f.DetectedFrom; }
                else if (f.Kind == "post-body") { newBody = f.Translated; postFrom ??= f.DetectedFrom; }
                else if (f.Kind.StartsWith("comment:", StringComparison.Ordinal))
                {
                    var cid = int.Parse(f.Kind.Substring("comment:".Length));
                    _db.CommentTranslations.Add(new CommentTranslation
                    {
                        CommentId = cid,
                        LanguageCode = target,
                        DetectedFromLanguage = f.DetectedFrom,
                        BodyTranslated = f.Translated,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            if (newBody != null)
            {
                _db.PostTranslations.Add(new PostTranslation
                {
                    PostId = postId,
                    LanguageCode = target,
                    DetectedFromLanguage = postFrom,
                    TitleTranslated = newTitle,
                    BodyTranslated = newBody,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync(ct);
        }

        // Merge cached + fresh into the result.
        string titleOut;
        string bodyOut;
        string? fromLang;
        if (postCache != null)
        {
            titleOut = postCache.TitleTranslated ?? string.Empty;
            bodyOut = postCache.BodyTranslated;
            fromLang = postCache.DetectedFromLanguage;
        }
        else
        {
            titleOut = fresh.FirstOrDefault(f => f.Kind == "post-title").Translated ?? string.Empty;
            bodyOut = fresh.FirstOrDefault(f => f.Kind == "post-body").Translated ?? string.Empty;
            fromLang = fresh.FirstOrDefault(f => f.Kind == "post-body").DetectedFrom
                       ?? fresh.FirstOrDefault(f => f.Kind == "post-title").DetectedFrom;
        }

        var translatedComments = new List<TranslatedComment>();
        foreach (var c in comments)
        {
            if (commentCaches.TryGetValue(c.Id, out var cached))
            {
                translatedComments.Add(new TranslatedComment(c.Id, cached.BodyTranslated));
            }
            else
            {
                var f = fresh.FirstOrDefault(x => x.Kind == "comment:" + c.Id);
                if (f.Kind != null)
                    translatedComments.Add(new TranslatedComment(c.Id, f.Translated));
            }
        }

        return new PostTranslationResult(
            Title: string.IsNullOrWhiteSpace(titleOut) ? null : titleOut,
            Body: bodyOut,
            FromLanguage: fromLang,
            Comments: translatedComments);
    }
}
