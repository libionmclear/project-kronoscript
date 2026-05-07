namespace MyStoryTold.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Persist a file and return the URL to use in &lt;img src&gt; / DB rows.
    /// In production the URL is the public Blob URL; in local dev (no
    /// connection string set) it falls back to a /uploads/... path served by
    /// the static-files middleware so existing code paths still work.
    /// </summary>
    /// <param name="content">Source stream — caller is responsible for disposing.</param>
    /// <param name="folder">Subfolder/prefix (e.g. "profiles", "profile-bg", or "" for root).</param>
    /// <param name="fileName">File name including extension (e.g. "abc.jpg").</param>
    /// <param name="contentType">MIME type for the upload. Defaults to application/octet-stream when null.</param>
    Task<string> UploadAsync(Stream content, string folder, string fileName, string? contentType, CancellationToken ct = default);
}
