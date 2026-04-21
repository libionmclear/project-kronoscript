namespace MyStoryTold.Services;

public record TranslatedComment(int CommentId, string Body);

public record PostTranslationResult(
    string? Title,
    string Body,
    string? FromLanguage,
    IReadOnlyList<TranslatedComment> Comments);

public interface ITranslationService
{
    /// <summary>
    /// Translates a post's title + body and every comment on that post into the
    /// target language. Cache-first per row; uncached entries go to Azure in a
    /// single batch, results are persisted, and the merged view is returned.
    /// </summary>
    Task<PostTranslationResult> TranslatePostAsync(int postId, string targetLanguage, CancellationToken ct = default);
}
