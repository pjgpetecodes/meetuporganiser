using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MeetupOrganiser.Infrastructure.Services;

public class SocialImageGeneratorService : ISocialImageGeneratorService
{
    private readonly ILogger<SocialImageGeneratorService> _logger;

    public SocialImageGeneratorService(ILogger<SocialImageGeneratorService> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> GenerateSocialCardAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default)
    {
        const int width = 1280;
        const int height = 720;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var isTwoSpeakers = sessions.Count > 1;

        RemotePhotos.Value = await PreloadRemotePhotosAsync(sessions, cancellationToken);

        // 1. Draw Teal Radial Background Gradient matching .NET Notts slide format
        // Teal gradient: center at top-right (1010, 90), color #1ABC9C radiating to #11816B at corners
        var center = new SKPoint(1010, 90);
        var colors = new[] { new SKColor(26, 188, 156), new SKColor(17, 129, 107) };
        var colorPos = new[] { 0f, 1f };

        using (var bgShader = SKShader.CreateRadialGradient(center, 1200, colors, colorPos, SKShaderTileMode.Clamp))
        using (var bgPaint = new SKPaint { Shader = bgShader, IsAntialias = true })
        {
            canvas.DrawRect(0, 0, width, height, bgPaint);
        }

        // 2. Draw Top-Right Sponsor Banner
        DrawSponsorsBanner(canvas, isTwoSpeakers);

        // 3. Render either 1 Speaker or 2 Speakers Layout
        if (!isTwoSpeakers)
        {
            RenderSingleSpeakerLayout(canvas, meetupEvent, sessions.FirstOrDefault());
        }
        else
        {
            RenderTwoSpeakersLayout(canvas, meetupEvent, sessions[0], sessions[1]);
        }

        // 4. Bottom Short Link (e.g. https://bit.ly/dotnetnottsapril2026)
        var shortUrl = meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "https://meetup.com/dotnetnotts";
        using (var fontUrl = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 26))
        using (var urlPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            var textWidth = fontUrl.MeasureText(shortUrl, urlPaint);
            canvas.DrawText(shortUrl, (width - textWidth) / 2f, 690, SKTextAlign.Left, fontUrl, urlPaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return data.ToArray();
    }

    private static readonly AsyncLocal<Dictionary<string, byte[]>?> RemotePhotos = new();

    private static async Task<Dictionary<string, byte[]>> PreloadRemotePhotosAsync(IEnumerable<EventSpeakerSession> sessions, CancellationToken cancellationToken)
    {
        var photos = new Dictionary<string, byte[]>();
        foreach (var url in sessions.Select(s => s.Speaker?.PhotoBlobUrl).Where(RemotePhotoLoader.IsRemote).Distinct())
        {
            var bytes = await RemotePhotoLoader.LoadAsync(url, cancellationToken);
            if (bytes != null) photos[url!] = bytes;
        }

        return photos;
    }

    private static void RenderSingleSpeakerLayout(SKCanvas canvas, MeetupEvent meetupEvent, EventSpeakerSession? session)
    {
        var speaker = session?.Speaker;
        var speakerName = speaker?.Name ?? "Luce Carter";
        var talkTitle = session?.TalkTitle ?? "SQL to MongoDB API with C#";

        // Header lines: date, format-dependent venue line, and an optional subtitle (only drawn when set)
        var dateFormatted = EventHeader.DateLine(meetupEvent);
        var line2 = EventHeader.VenueLine(meetupEvent);

        using (var fontHeaderBold = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 44))
        using (var textPaintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(dateFormatted, 60, 65, SKTextAlign.Left, fontHeaderBold, textPaintWhite);
            canvas.DrawText(line2, 60, 125, SKTextAlign.Left, fontHeaderBold, textPaintWhite);
        }

        var subtitle = EventHeader.Subtitle(meetupEvent);
        if (subtitle != null)
        {
            using var fontSubtitle = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 44);
            using var textPaintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(subtitle, 60, 185, SKTextAlign.Left, fontSubtitle, textPaintWhite);
        }

        // Speaker Avatar Circle with Drop Shadow
        // Center: (274, 399), Radius: 180
        const float avatarCenterX = 274f;
        const float avatarCenterY = 399f;
        const float avatarRadius = 180f;

        // Draw soft ellipse shadow beneath the circle
        using (var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 50, 40, 140),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 22f)
        })
        {
            canvas.DrawOval(avatarCenterX, avatarCenterY + avatarRadius + 32, avatarRadius * 0.95f, 24f, shadowPaint);
        }

        // Avatar photo or placeholder
        DrawCircularAvatar(canvas, speaker?.PhotoBlobUrl, speakerName, avatarCenterX, avatarCenterY, avatarRadius);

        // Speaker Name and Talk Title on Right
        // Name: Luce Carter (font size ~68pt)
        // Talk: SQL to MongoDB API with C# (font size ~38pt)
        using var fontSpeaker = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 68);
        using var fontTalk = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 38);
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        // Keep the name + wrapped title block vertically centred on the avatar
        var talkLines = WrapLines(talkTitle, 720, 3, fontTalk).Count;
        var shift = (Math.Max(talkLines, 1) - 1) * fontTalk.Size * 1.25f / 2f;
        canvas.DrawText(speakerName, 520, 395 - shift, SKTextAlign.Left, fontSpeaker, textPaint);
        DrawWrappedText(canvas, talkTitle, 520, 465 - shift, 720, 3, fontTalk, textPaint);
    }
    private static void DrawWrappedText(SKCanvas canvas, string text, float x, float y, float maxWidth, int maxLines, SKFont font, SKPaint paint)
    {
        var lines = WrapLines(text, maxWidth, maxLines, font);
        var lineHeight = font.Size * 1.25f;
        for (var i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], x, y + i * lineHeight, SKTextAlign.Left, font, paint);
        }
    }

    private static List<string> WrapLines(string text, float maxWidth, int maxLines, SKFont font)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && font.MeasureText(candidate) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0) lines.Add(current);

        if (lines.Count > maxLines)
        {
            var kept = lines.Take(maxLines).ToList();
            var last = kept[^1];
            while (last.Length > 1 && font.MeasureText(last + "…") > maxWidth) last = last[..^1];
            kept[^1] = last.TrimEnd() + "…";
            lines = kept;
        }

        return lines;
    }
    private static void RenderTwoSpeakersLayout(SKCanvas canvas, MeetupEvent meetupEvent, EventSpeakerSession session1, EventSpeakerSession session2)
    {
        // Top-left header: "TONIGHT!" (font size ~68pt, Bold)
        using (var fontTonight = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 72))
        using (var textPaintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText("TONIGHT!", 60, 95, SKTextAlign.Left, fontTonight, textPaintWhite);
        }

        // Speaker 1 (Left circle, centered details)
        // Center: (195, 435), Radius: 120
        var sp1 = session1.Speaker;
        var name1 = sp1?.Name ?? "Speaker 1";
        var talk1 = session1.TalkTitle;

        // Shadow 1
        using (var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 50, 40, 140),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18f)
        })
        {
            canvas.DrawOval(195f, 435f + 120f + 25f, 105f, 18f, shadowPaint);
        }
        DrawCircularAvatar(canvas, sp1?.PhotoBlobUrl, name1, 195f, 435f, 120f);

        // Details 1
        using (var fontName1 = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 48))
        using (var fontTalk1 = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 30))
        using (var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(name1, 350, 395, SKTextAlign.Left, fontName1, textPaint);
            DrawWrappedText(canvas, talk1, 350, 450, 560, 2, fontTalk1, textPaint);
        }

        // Speaker 2 (Right circle, details on left of avatar or center)
        // Center: (1085, 595), Radius: 120
        var sp2 = session2.Speaker;
        var name2 = sp2?.Name ?? "Speaker 2";
        var talk2 = session2.TalkTitle;

        // Details 2
        using (var fontName2 = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 48))
        using (var fontTalk2 = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 30))
        using (var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(name2, 530, 550, SKTextAlign.Left, fontName2, textPaint);
            DrawWrappedText(canvas, talk2, 450, 605, 480, 2, fontTalk2, textPaint);
        }

        DrawCircularAvatar(canvas, sp2?.PhotoBlobUrl, name2, 1085f, 595f, 120f);
    }

    private static void DrawCircularAvatar(SKCanvas canvas, string? photoUrl, string speakerName, float cx, float cy, float radius)
    {
        // Pure white border ring
        using (var borderPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f
        })
        {
            canvas.DrawCircle(cx, cy, radius + 2f, borderPaint);
        }

        var photoBitmap = LoadSpeakerBitmap(photoUrl);
        if (photoBitmap != null)
        {
            using (photoBitmap)
            using (var circlePath = new SKPath())
            {
                circlePath.AddCircle(cx, cy, radius);
                canvas.Save();
                canvas.ClipPath(circlePath, antialias: true);
                var destRect = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
                canvas.DrawBitmap(photoBitmap, destRect);
                canvas.Restore();
            }
        }
        else
        {
            using var placeholderPaint = new SKPaint { Color = new SKColor(45, 55, 72), IsAntialias = true };
            canvas.DrawCircle(cx, cy, radius, placeholderPaint);

            var initials = string.Concat(speakerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s[0]));
            if (string.IsNullOrEmpty(initials)) initials = "SP";

            using var fontInit = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), radius * 0.55f);
            using var initPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var iw = fontInit.MeasureText(initials, initPaint);
            canvas.DrawText(initials, cx - iw / 2, cy + radius * 0.2f, SKTextAlign.Left, fontInit, initPaint);
        }
    }

    private static void DrawSponsorsBanner(SKCanvas canvas, bool isTwoSpeakers)
    {
        try
        {
            var fileName = isTwoSpeakers ? "sponsors-banner-2speakers.png" : "sponsors-banner.png";
            var bannerPaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Infrastructure", "Resources", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Web", "wwwroot", "images", "social", fileName),
                Path.Combine(AppContext.BaseDirectory, "Resources", fileName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MeetupOrganiser.Infrastructure", "Resources", fileName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MeetupOrganiser.Web", "wwwroot", "images", "social", fileName)
            };

            var validPath = bannerPaths.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
            if (validPath != null)
            {
                using var bannerBitmap = SKBitmap.Decode(validPath);
                if (bannerBitmap != null)
                {
                    // Position at top right
                    // Dimensions: ~340x160 at X: 920, Y: 15
                    var destRect = new SKRect(1280 - bannerBitmap.Width - 15, 12, 1280 - 15, 12 + bannerBitmap.Height);
                    canvas.DrawBitmap(bannerBitmap, destRect);
                }
            }
        }
        catch
        {
            // Banner drawing is optional - don't fail rendering if not present
        }
    }

    private static SKBitmap? LoadSpeakerBitmap(string? photoUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(photoUrl))
            {
                var candidates = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "images", "speakers", "Luce Carter.jpg"),
                    Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Web", "wwwroot", "images", "speakers", "Luce Carter.jpg"),
                    Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "speakers", "Luce Carter.jpg")
                };
                var found = candidates.FirstOrDefault(File.Exists);
                if (found != null)
                {
                    return DecodeOriented(File.ReadAllBytes(found));
                }
                return null;
            }

            if (RemotePhotoLoader.IsRemote(photoUrl))
            {
                return RemotePhotos.Value != null && RemotePhotos.Value.TryGetValue(photoUrl, out var remote)
                    ? DecodeOriented(remote)
                    : null;
            }

            if (File.Exists(photoUrl))
            {
                return DecodeOriented(File.ReadAllBytes(photoUrl));
            }

            var localRel = photoUrl.TrimStart('/', '\\');
            var localCandidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", localRel),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Web", "wwwroot", localRel),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", localRel),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MeetupOrganiser.Web", "wwwroot", localRel),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "images", "speakers", Path.GetFileName(photoUrl))
            };

            var valid = localCandidates.FirstOrDefault(File.Exists);
            if (valid != null)
            {
                return DecodeOriented(File.ReadAllBytes(valid));
            }
        }
        catch
        {
            // Fallback gracefully
        }

        return null;
    }

    // SkiaSharp ignores EXIF orientation on decode, so apply it manually.
    private static SKBitmap? DecodeOriented(byte[] data)
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
}