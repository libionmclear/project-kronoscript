using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.Processing;

namespace MyStoryTold.Services;

public interface IImageProcessor
{
    /// <summary>
    /// Returns true if the content type / extension hints at an image we'll process.
    /// Videos and other non-image uploads should bypass the pipeline.
    /// </summary>
    bool ShouldProcess(string? contentType, string? fileName);

    /// <summary>
    /// Re-encodes the input image with EXIF / GPS / IPTC / XMP metadata stripped
    /// and dimensions capped at <paramref name="maxDimension"/> pixels on the long
    /// side. Returns a new MemoryStream the caller is responsible for disposing.
    /// </summary>
    Task<(Stream content, string contentType, string fileName)> ProcessAsync(
        Stream input, string fileName, int maxDimension = 2000, CancellationToken ct = default);
}

public class ImageProcessor : IImageProcessor
{
    private static readonly string[] ImageContentTypes =
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif", "image/bmp"
    };

    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif", ".bmp"
    };

    public bool ShouldProcess(string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            foreach (var t in ImageContentTypes)
                if (contentType.Equals(t, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            foreach (var e in ImageExtensions)
                if (ext == e) return true;
        }
        return false;
    }

    public async Task<(Stream content, string contentType, string fileName)> ProcessAsync(
        Stream input, string fileName, int maxDimension = 2000, CancellationToken ct = default)
    {
        if (input.CanSeek) input.Position = 0;

        using var image = await Image.LoadAsync(input, ct);

        // Strip every metadata profile that could leak GPS / camera serial / etc.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;

        // Auto-orient based on the original EXIF Orientation, then drop EXIF (already null).
        image.Mutate(x => x.AutoOrient());

        // Cap longest side at maxDimension. Mode=Max preserves aspect ratio.
        if (image.Width > maxDimension || image.Height > maxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxDimension, maxDimension),
                Mode = ResizeMode.Max
            }));
        }

        // Re-encode as JPEG quality 85. Photos compress fine at this setting and we
        // standardize the output type so blob URLs and content-types are consistent.
        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 }, ct);
        output.Position = 0;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem)) stem = Guid.NewGuid().ToString("N");
        return (output, "image/jpeg", $"{stem}.jpg");
    }
}
