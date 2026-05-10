namespace MyStoryTold.Helpers;

/// <summary>
/// Magic-byte sniffer for uploaded media. Validates that the file's
/// actual content matches the declared MIME / extension before we
/// accept it — a browser-supplied ContentType is user-controlled and
/// can lie. Without this check an attacker could upload a .jpg
/// containing executable HTML/JS or a binary payload and have the
/// platform happily serve it.
///
/// Only the formats Kronoscript actually accepts are listed; anything
/// else returns Unknown and the caller rejects the upload.
/// </summary>
public enum FileSignatureKind
{
    Unknown,
    Jpeg, Png, Gif, Webp,
    Mp4, Mov, Webm, Avi
}

public static class FileSignatures
{
    /// <summary>Inspect the first 12+ bytes of a file and return the
    /// detected signature. Pass at least 12 bytes; 16 is a safe upper.</summary>
    public static FileSignatureKind Detect(byte[] header)
    {
        if (header == null || header.Length < 12) return FileSignatureKind.Unknown;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return FileSignatureKind.Jpeg;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return FileSignatureKind.Png;

        // GIF87a / GIF89a
        if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 &&
            (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
            return FileSignatureKind.Gif;

        // RIFF container — could be WebP or AVI; subtype lives at offset 8.
        if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
        {
            // "WEBP"
            if (header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return FileSignatureKind.Webp;
            // "AVI "
            if (header[8] == 0x41 && header[9] == 0x56 && header[10] == 0x49 && header[11] == 0x20)
                return FileSignatureKind.Avi;
        }

        // MP4 / MOV: "ftyp" at offset 4. The 4-byte brand at offset 8
        // tells us which: "qt  " = QuickTime/MOV, anything else we
        // accept as MP4 (covers isom, avc1, mp41, mp42, M4V, etc.).
        if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
        {
            if (header[8] == 0x71 && header[9] == 0x74)
                return FileSignatureKind.Mov;
            return FileSignatureKind.Mp4;
        }

        // WebM / Matroska — EBML header
        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
            return FileSignatureKind.Webm;

        return FileSignatureKind.Unknown;
    }

    public static bool IsImage(FileSignatureKind k) =>
        k is FileSignatureKind.Jpeg or FileSignatureKind.Png or FileSignatureKind.Gif or FileSignatureKind.Webp;

    public static bool IsVideo(FileSignatureKind k) =>
        k is FileSignatureKind.Mp4 or FileSignatureKind.Mov or FileSignatureKind.Webm or FileSignatureKind.Avi;

    /// <summary>Read the first 16 bytes from a stream non-destructively
    /// (resets position) and return the signature. Returns Unknown if
    /// the stream is shorter than 12 bytes.</summary>
    public static async Task<FileSignatureKind> DetectAsync(Stream stream)
    {
        var buf = new byte[16];
        var read = 0;
        // Read up to 16 bytes — IFormFile read streams should give us
        // that without trouble. If we can seek, restore position so
        // the caller can re-read for the actual upload.
        var origPos = stream.CanSeek ? stream.Position : -1;
        while (read < 16)
        {
            var n = await stream.ReadAsync(buf.AsMemory(read, 16 - read));
            if (n <= 0) break;
            read += n;
        }
        if (origPos >= 0) stream.Position = origPos;
        return read >= 12 ? Detect(buf) : FileSignatureKind.Unknown;
    }

    /// <summary>The canonical extension for a detected signature. Used
    /// to overwrite the user-supplied filename so we never store a
    /// .jpg.exe-style mismatch.</summary>
    public static string ExtensionFor(FileSignatureKind k) => k switch
    {
        FileSignatureKind.Jpeg => ".jpg",
        FileSignatureKind.Png  => ".png",
        FileSignatureKind.Gif  => ".gif",
        FileSignatureKind.Webp => ".webp",
        FileSignatureKind.Mp4  => ".mp4",
        FileSignatureKind.Mov  => ".mov",
        FileSignatureKind.Webm => ".webm",
        FileSignatureKind.Avi  => ".avi",
        _ => ""
    };

    /// <summary>Canonical content-type for a detected signature.</summary>
    public static string MimeFor(FileSignatureKind k) => k switch
    {
        FileSignatureKind.Jpeg => "image/jpeg",
        FileSignatureKind.Png  => "image/png",
        FileSignatureKind.Gif  => "image/gif",
        FileSignatureKind.Webp => "image/webp",
        FileSignatureKind.Mp4  => "video/mp4",
        FileSignatureKind.Mov  => "video/quicktime",
        FileSignatureKind.Webm => "video/webm",
        FileSignatureKind.Avi  => "video/x-msvideo",
        _ => "application/octet-stream"
    };
}
