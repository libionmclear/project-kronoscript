using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MyStoryTold.Services;

public class AzureBlobFileStorageService : IFileStorageService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AzureBlobFileStorageService> _logger;
    private readonly Lazy<BlobContainerClient?> _containerClient;

    private const string DefaultContainerName = "uploads";

    public AzureBlobFileStorageService(IConfiguration config, IWebHostEnvironment env, ILogger<AzureBlobFileStorageService> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
        _containerClient = new Lazy<BlobContainerClient?>(BuildContainerClient);
    }

    private BlobContainerClient? BuildContainerClient()
    {
        var connStr = _config["Azure:Storage:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connStr)) return null;
        var containerName = _config["Azure:Storage:Container"] ?? DefaultContainerName;
        try
        {
            return new BlobContainerClient(connStr, containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build BlobContainerClient — falling back to local storage.");
            return null;
        }
    }

    public async Task<string> UploadAsync(Stream content, string folder, string fileName, string? contentType, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("fileName required", nameof(fileName));

        var blobKey = string.IsNullOrEmpty(folder) ? fileName : $"{folder.Trim('/')}/{fileName}";
        var container = _containerClient.Value;

        if (container != null)
        {
            try
            {
                var blob = container.GetBlobClient(blobKey);
                var headers = new BlobHttpHeaders { ContentType = contentType ?? "application/octet-stream" };
                content.Position = content.CanSeek ? 0 : content.Position;
                await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = headers }, ct);
                return blob.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blob upload failed for {Key}; falling back to local storage.", blobKey);
                // fall through to local
            }
        }

        // Local-fallback path so dev without an Azure connection still works.
        // Existing app code/serves /uploads/ via static files; preserve that shape.
        var rootFolder = string.IsNullOrEmpty(folder)
            ? Path.Combine(_env.WebRootPath, "uploads")
            : Path.Combine(_env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(rootFolder);
        var localPath = Path.Combine(rootFolder, fileName);

        if (content.CanSeek) content.Position = 0;
        await using (var fs = new FileStream(localPath, FileMode.Create))
        {
            await content.CopyToAsync(fs, ct);
        }

        return string.IsNullOrEmpty(folder)
            ? $"/uploads/{fileName}"
            : $"/uploads/{folder}/{fileName}";
    }
}
