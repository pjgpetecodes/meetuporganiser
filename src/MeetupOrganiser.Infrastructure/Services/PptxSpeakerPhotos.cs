using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using MeetupOrganiser.Core.Entities;
using SkiaSharp;
using D = DocumentFormat.OpenXml.Drawing;

namespace MeetupOrganiser.Infrastructure.Services;

/// <summary>Swaps the speaker headshot pictures on a deck slide, whichever template layout the group uses.</summary>
internal static class PptxSpeakerPhotos
{
    private const long MinPhotoSizeEmu = 914400;
    private const int PhotoSize = 600;

    public static async Task<byte[]> LoadAsync(Speaker? speaker, CancellationToken cancellationToken = default)
    {
        var bytes = PptxGeneratorService.LoadSpeakerBytes(speaker?.PhotoBlobUrl)
            ?? await RemotePhotoLoader.LoadAsync(speaker?.PhotoBlobUrl, cancellationToken);
        return (bytes != null ? ToSquarePng(bytes) : null) ?? Placeholder(speaker?.Name);
    }

    // Pictures whose alt text is {{Speaker1Photo}} / {{SpeakerPhoto}} / {{Speaker2Photo}} are swapped directly; decks without markers fall back to size and position.
    public static void Apply(SlidePart slidePart, PptxTokens.SlideKind kind, IReadOnlyList<byte[]> photos)
    {
        if (slidePart.Slide == null || photos.Count == 0) return;

        var marked = false;
        foreach (var pic in slidePart.Slide.Descendants<Picture>())
        {
            var index = PhotoIndex(pic);
            if (index == null) continue;

            marked = true;
            if (index < photos.Count) Replace(slidePart, pic, photos[index.Value]);
        }

        if (marked || kind == PptxTokens.SlideKind.Always) return;

        var slots = slidePart.Slide.Descendants<Picture>()
            .Select(pic => (Pic: pic, Xfrm: pic.ShapeProperties?.Transform2D))
            .Where(item => item.Xfrm?.Offset != null && item.Xfrm.Extents != null && item.Pic.BlipFill?.Blip != null)
            .Select(item => (item.Pic, X: item.Xfrm!.Offset!.X!.Value, W: item.Xfrm.Extents!.Cx!.Value, H: item.Xfrm.Extents.Cy!.Value))
            .Where(item => item.W >= MinPhotoSizeEmu && item.H >= MinPhotoSizeEmu
                && item.W / (double)item.H is > 0.75 and < 1.33)
            .ToList();

        var chosen = kind == PptxTokens.SlideKind.TwoSpeakers
            ? slots.OrderByDescending(item => item.W * item.H).Take(2).OrderBy(item => item.X).ToList()
            : slots.OrderByDescending(item => item.W * item.H).Take(1).ToList();

        for (var index = 0; index < chosen.Count && index < photos.Count; index++)
        {
            Replace(slidePart, chosen[index].Pic, photos[index]);
        }
    }

    private static int? PhotoIndex(Picture pic)
    {
        var props = pic.NonVisualPictureProperties?.NonVisualDrawingProperties;
        foreach (var text in new[] { props?.Description?.Value, props?.Title?.Value })
        {
            var normalised = text?.Replace(" ", string.Empty).Trim();
            if (string.IsNullOrEmpty(normalised)) continue;
            if (normalised.Equals("{{Speaker1Photo}}", StringComparison.OrdinalIgnoreCase)
                || normalised.Equals("{{SpeakerPhoto}}", StringComparison.OrdinalIgnoreCase)
                || normalised.Equals("{{NextEventSpeaker1Photo}}", StringComparison.OrdinalIgnoreCase)
                || normalised.Equals("{{NextEventSpeakerPhoto}}", StringComparison.OrdinalIgnoreCase)) return 0;
            if (normalised.Equals("{{Speaker2Photo}}", StringComparison.OrdinalIgnoreCase)
                || normalised.Equals("{{NextEventSpeaker2Photo}}", StringComparison.OrdinalIgnoreCase)) return 1;
        }

        return null;
    }

    private static void Replace(SlidePart slidePart, Picture pic, byte[] photo)
    {
        var blipFill = pic.BlipFill;
        if (blipFill?.Blip == null) return;

        var imagePart = slidePart.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(photo))
        {
            imagePart.FeedData(stream);
        }

        blipFill.Blip.Embed = slidePart.GetIdOfPart(imagePart);
        blipFill.RemoveAllChildren<D.SourceRectangle>();
    }

    private static byte[]? ToSquarePng(byte[] data)
    {
        try
        {
            using var bitmap = ImageOrientation.Decode(data);
            if (bitmap == null) return null;

            var side = Math.Min(bitmap.Width, bitmap.Height);
            var source = SKRect.Create((bitmap.Width - side) / 2f, (bitmap.Height - side) / 2f, side, side);
            using var target = new SKBitmap(PhotoSize, PhotoSize);
            using var canvas = new SKCanvas(target);
            using var paint = new SKPaint { IsAntialias = true };
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, source, SKRect.Create(PhotoSize, PhotoSize), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            using var encoded = SKImage.FromBitmap(target).Encode(SKEncodedImageFormat.Png, 90);
            return encoded.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Placeholder(string? name)
    {
        using var bitmap = new SKBitmap(PhotoSize, PhotoSize);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(45, 55, 72));

        var initials = string.Concat((name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
        if (initials.Length == 0) initials = "SP";

        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), PhotoSize * 0.38f);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var width = font.MeasureText(initials, paint);
        canvas.DrawText(initials, (PhotoSize - width) / 2f, PhotoSize * 0.64f, SKTextAlign.Left, font, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }
}
