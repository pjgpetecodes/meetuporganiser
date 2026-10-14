using SkiaSharp;

namespace MeetupOrganiser.Infrastructure.Services;

internal static class ImageOrientation
{
    // SkiaSharp ignores EXIF orientation on decode, so apply it manually.
    public static SKBitmap? Decode(byte[] data)
    {
        using var codec = SKCodec.Create(new MemoryStream(data));
        var bitmap = SKBitmap.Decode(data);
        if (codec == null || bitmap == null) return bitmap;

        var origin = codec.EncodedOrigin;
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? bitmap.Height : bitmap.Width;
        var h = swap ? bitmap.Width : bitmap.Height;
        var result = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear();
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.SetMatrix(new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1)); break;
                case SKEncodedOrigin.RightTop: canvas.SetMatrix(new SKMatrix(0, -1, bitmap.Height, 1, 0, 0, 0, 0, 1)); break;
                case SKEncodedOrigin.RightBottom: canvas.SetMatrix(new SKMatrix(0, -1, bitmap.Height, -1, 0, bitmap.Width, 0, 0, 1)); break;
                case SKEncodedOrigin.LeftBottom: canvas.SetMatrix(new SKMatrix(0, 1, 0, -1, 0, bitmap.Width, 0, 0, 1)); break;
            }

            canvas.DrawBitmap(bitmap, 0, 0);
        }

        bitmap.Dispose();
        return result;
    }

    // Returns upright image bytes (re-encoded, EXIF stripped) when the image needs rotating; otherwise the original bytes.
    public static byte[] Normalize(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new MemoryStream(data));
            if (codec == null || codec.EncodedOrigin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return data;

            using var bitmap = Decode(data);
            if (bitmap == null) return data;
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(codec.EncodedFormat == SKEncodedImageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 92);
            return encoded.ToArray();
        }
        catch
        {
            return data;
        }
    }
}
