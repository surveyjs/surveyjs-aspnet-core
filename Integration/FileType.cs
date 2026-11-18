namespace SurveyJsAspNetCore.Integration;

/// <summary>
/// The type of an uploaded file, detected from its first bytes. The client's Content-Type and file name are whatever
/// the client says, so store-files and extract-from-paper never trust them.
/// </summary>
public static class FileType
{
    /// <summary>"image/png", "image/jpeg", "image/webp", "image/gif", "application/pdf", or null for anything else.</summary>
    public static string? Detect(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => "image/gif",
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', ..] => "application/pdf",
        _ => null,
    };

    /// <summary>Reads an upload into memory: the endpoints check sizes first, so it is at most a few MB.</summary>
    public static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken aborted)
    {
        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, aborted);
        return stream.ToArray();
    }
}
