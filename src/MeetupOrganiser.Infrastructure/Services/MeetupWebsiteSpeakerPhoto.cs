using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using SkiaSharp;

namespace MeetupOrganiser.Infrastructure.Services;

public static class MeetupWebsiteSpeakerPhoto
{
    public static async Task<MeetupWebsiteHandoff> AttachAsync(MeetupWebsiteHandoff payload, MeetupEvent meetup,
        CancellationToken cancellationToken = default)
    {
        if (payload.Speaker is null) return payload;
        var speaker = meetup.Sessions.OrderBy(s => s.OrderIndex)
            .Select(s => s.Speaker).FirstOrDefault(s => s is { IsPlaceholder: false });
        var url = speaker?.PhotoBlobUrl;
        if (string.IsNullOrWhiteSpace(url))
            return WithError(payload, "No saved speaker photo is available. Upload one to the speaker profile first.");

        byte[]? bytes;
        try
        {
            bytes = RemotePhotoLoader.IsRemote(url)
                ? await RemotePhotoLoader.LoadAsync(url, cancellationToken)
                : await LoadLocalAsync(url, cancellationToken);
        }
        catch (IOException)
        {
            return WithError(payload, "The saved speaker photo could not be read. Upload it manually or update the speaker profile.");
        }
        catch (UnauthorizedAccessException)
        {
            return WithError(payload, "The saved speaker photo is not accessible. Upload it manually or update the speaker profile.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes is null)
            return WithError(payload, "The saved speaker photo could not be loaded safely. Download/upload it manually or update the speaker profile.");
        try
        {
            return payload with { Speaker = payload.Speaker with { PhotoBase64 = Convert.ToBase64String(ToPng(bytes)) } };
        }
        catch (ArgumentException ex) { return WithError(payload, ex.Message); }
    }

    public static byte[] ToPng(byte[] bytes)
    {
        if (bytes.Length > MeetupWebsite.MaximumImageBytes)
            throw new ArgumentException("Speaker photo exceeds the supported 5 MB limit. Upload a smaller photo.");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Gif) ||
            codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 24_000_000)
            throw new ArgumentException("Speaker photo must be a valid JPEG, PNG or GIF of at most 24 megapixels.");
        using var bitmap = ImageOrientation.Decode(bytes);
        if (bitmap is null) throw new ArgumentException("Unable to decode the saved speaker photo. Upload a different photo.");
        using var source = SKImage.FromBitmap(bitmap);
        for (var maximumSide = 1600; maximumSide >= 200; maximumSide /= 2)
        {
            var scale = Math.Min(1d, maximumSide / (double)Math.Max(bitmap.Width, bitmap.Height));
            using var output = new SKBitmap(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
            using var canvas = new SKCanvas(output);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(source, SKRect.Create(output.Width, output.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var image = SKImage.FromBitmap(output);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded is not null && encoded.Size <= 1024 * 1024) return encoded.ToArray();
        }
        throw new ArgumentException("The converted speaker photo is too large. Upload a smaller photo.");
    }

    private static MeetupWebsiteHandoff WithError(MeetupWebsiteHandoff payload, string message) =>
        payload with { Speaker = payload.Speaker! with { PhotoError = message } };

    private static async Task<byte[]?> LoadLocalAsync(string url, CancellationToken cancellationToken)
    {
        var relative = url.Replace('\\', '/').TrimStart('/');
        if (!relative.StartsWith("images/speakers/", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var root in new[] {
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Web", "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot")
        })
        {
            var directory = Path.GetFullPath(Path.Combine(root, "images", "speakers")) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            await using var stream = File.OpenRead(path);
            if (stream.Length > MeetupWebsite.MaximumImageBytes) return null;
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MeetupWebsite.MaximumImageBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        return null;
    }
}
