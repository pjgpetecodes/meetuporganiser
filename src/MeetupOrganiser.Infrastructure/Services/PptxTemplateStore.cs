using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public static class PptxTemplateStore
{
    // Stored template URLs end in /{container}/{file}; the file name is a "<guid>_name.pptx" blob.
    public static bool TryParseFileName(string? url, out string fileName)
    {
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var segments = url.Split('?')[0].TrimEnd('/').Split('/');
        if (segments.Length < 2 || segments[^2] != TemplateContainer) return false;

        fileName = Uri.UnescapeDataString(segments[^1]);
        return IsValidFileName(fileName);
    }

    public const string TemplateContainer = "templates";

    public static bool IsValidFileName(string fileName) =>
        fileName.Length is > 0 and < 300
        && fileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
        && fileName == Path.GetFileName(fileName)
        && !fileName.Contains("..");

    public static async Task<byte[]?> LoadAsync(IBlobStorageService blobStorage, string fileName, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await blobStorage.DownloadAsync(TemplateContainer, fileName, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load PPTX template {FileName}.", fileName);
            return null;
        }
    }
}
