using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
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

    public Task<byte[]> GenerateSocialCardAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default)
    {
        const int width = 1280;
        const int height = 720;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var isTwoSpeakers = sessions.Count > 1;

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
        return Task.FromResult(data.ToArray());
    }

    private static void RenderSingleSpeakerLayout(SKCanvas canvas, MeetupEvent meetupEvent, EventSpeakerSession? session)
    {
        var speaker = session?.Speaker;
        var speakerName = speaker?.Name ?? "Luce Carter";
        var talkTitle = session?.TalkTitle ?? "SQL to MongoDB API with C#";

        // Date and Location Header:
        // Line 1: MONDAY 27TH APRIL 2026
        // Line 2: AT CGI ONLY FROM 18:30GMT!
        // Line 3: Special Workshop Edition!
        var dateFormatted = meetupEvent.EventDateTimeUtc.ToString("dddd d'TH' MMMM yyyy").ToUpperInvariant();
        var venue = (meetupEvent.LocationOrPlatform ?? "AT CGI ONLY").ToUpperInvariant();
        var timeFormatted = $"FROM {meetupEvent.EventDateTimeUtc:HH:mm}GMT!";
        var line2 = $"{venue} {timeFormatted}";

        using (var fontHeaderBold = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 44))
        using (var textPaintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(dateFormatted, 60, 65, SKTextAlign.Left, fontHeaderBold, textPaintWhite);
            canvas.DrawText(line2, 60, 125, SKTextAlign.Left, fontHeaderBold, textPaintWhite);
        }

        // Subtitle or Edition line
        var subtitle = "Special Workshop Edition!";
        using (var fontSubtitle = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 44))
        using (var textPaintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
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
        using (var fontSpeaker = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 68))
        using (var namePaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(speakerName, 520, 395, SKTextAlign.Left, fontSpeaker, namePaint);
        }

        using (var fontTalk = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 38))
        using (var talkPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(talkTitle, 520, 465, SKTextAlign.Left, fontTalk, talkPaint);
        }
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
            canvas.DrawText(talk1, 350, 450, SKTextAlign.Left, fontTalk1, textPaint);
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
            canvas.DrawText(talk2, 450, 605, SKTextAlign.Left, fontTalk2, textPaint);
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
                    return SKBitmap.Decode(found);
                }
                return null;
            }

            if (File.Exists(photoUrl))
            {
                return SKBitmap.Decode(photoUrl);
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
                return SKBitmap.Decode(valid);
            }
        }
        catch
        {
            // Fallback gracefully
        }

        return null;
    }
}
