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

        // 1. Background colors
        var group = meetupEvent.MeetupGroup;
        var primaryHex = group?.PrimaryColorHex ?? "#512BD4";
        var secondaryHex = group?.SecondaryColorHex ?? "#0078D4";

        if (!SKColor.TryParse(primaryHex, out var primaryColor))
            primaryColor = new SKColor(81, 43, 212);

        if (!SKColor.TryParse(secondaryHex, out var secondaryColor))
            secondaryColor = new SKColor(0, 120, 212);

        // Dark gradient background
        var darkBg = new SKColor(18, 18, 24);
        using (var bgPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [new SKColor(24, 26, 36), darkBg],
                [0f, 1f],
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRect(0, 0, width, height, bgPaint);
        }

        // Top decorative accent bar
        using (var accentPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, 0),
                [primaryColor, secondaryColor],
                [0f, 1f],
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRect(0, 0, width, 12, accentPaint);
        }

        // Decorative background geometric curves/circles
        using (var decPaint = new SKPaint
        {
            Color = primaryColor.WithAlpha(30),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3
        })
        {
            canvas.DrawCircle(1150, 150, 220, decPaint);
            canvas.DrawCircle(1200, 100, 160, decPaint);
            canvas.DrawCircle(100, 650, 180, decPaint);
        }

        // 2. Header: Group Name & Tagline
        var groupName = group?.Name ?? ".NET Notts";
        using (var fontGroup = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 42))
        using (var groupPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(groupName, 60, 80, SKTextAlign.Left, fontGroup, groupPaint);
        }

        var tagline = group?.Description?.Split('\n').FirstOrDefault() ?? "Talks from the world of Microsoft and beyond";
        if (tagline.Length > 60) tagline = tagline[..57] + "...";
        using (var fontTag = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 22))
        using (var tagPaint = new SKPaint { Color = new SKColor(180, 185, 200), IsAntialias = true })
        {
            canvas.DrawText(tagline, 60, 115, SKTextAlign.Left, fontTag, tagPaint);
        }

        // 3. Event Date / Time / Platform pill
        var eventDateText = $"{meetupEvent.EventDateTimeUtc:dddd, MMMM d, yyyy} | {meetupEvent.EventDateTimeUtc:HH:mm} UTC";
        if (!string.IsNullOrWhiteSpace(meetupEvent.LocationOrPlatform))
        {
            eventDateText += $" | {meetupEvent.LocationOrPlatform}";
        }

        var pillRect = new SKRoundRect(new SKRect(60, 140, 60 + Math.Min(950, eventDateText.Length * 14 + 40), 185), 10, 10);
        using (var pillBgPaint = new SKPaint { Color = primaryColor.WithAlpha(50), IsAntialias = true })
        {
            canvas.DrawRoundRect(pillRect, pillBgPaint);
        }
        using (var pillBorderPaint = new SKPaint { Color = primaryColor.WithAlpha(180), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
        {
            canvas.DrawRoundRect(pillRect, pillBorderPaint);
        }
        using (var fontPill = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 20))
        using (var pillTextPaint = new SKPaint { Color = new SKColor(230, 235, 255), IsAntialias = true })
        {
            canvas.DrawText(eventDateText, 80, 172, SKTextAlign.Left, fontPill, pillTextPaint);
        }

        // 4. Speakers Section
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        if (sessions.Count <= 1)
        {
            // Single Speaker Layout
            DrawSingleSpeaker(canvas, sessions.FirstOrDefault(), 60, 220, width - 120, 380, primaryColor);
        }
        else
        {
            // Two Speakers Layout (side-by-side)
            var colWidth = (width - 150) / 2;
            DrawSpeakerCard(canvas, sessions[0], 60, 220, colWidth, 380, primaryColor, 1);
            DrawSpeakerCard(canvas, sessions[1], 80 + colWidth, 220, colWidth, 380, secondaryColor, 2);
        }

        // 5. Footer: RSVP Link & Hashtags
        var rsvpText = !string.IsNullOrWhiteSpace(meetupEvent.ShortBitlyUrl)
            ? $"RSVP: {meetupEvent.ShortBitlyUrl}"
            : (!string.IsNullOrWhiteSpace(meetupEvent.MeetupEventUrl) ? $"RSVP: {meetupEvent.MeetupEventUrl}" : "RSVP on Meetup");

        using (var footerBg = new SKPaint { Color = new SKColor(28, 30, 44), IsAntialias = true })
        {
            canvas.DrawRect(0, height - 70, width, 70, footerBg);
        }

        using (var fontRsvp = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 24))
        using (var rsvpPaint = new SKPaint { Color = new SKColor(96, 165, 250), IsAntialias = true })
        {
            canvas.DrawText(rsvpText, 60, height - 28, SKTextAlign.Left, fontRsvp, rsvpPaint);
        }

        var hashtags = group?.DefaultHashtags ?? "#dotnet #dotnetnotts #azure";
        using (var fontHash = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 20))
        using (var hashPaint = new SKPaint { Color = new SKColor(156, 163, 175), IsAntialias = true })
        {
            var hashWidth = fontHash.MeasureText(hashtags, hashPaint);
            canvas.DrawText(hashtags, width - hashWidth - 60, height - 28, SKTextAlign.Left, fontHash, hashPaint);
        }

        // Encode to PNG bytes
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return Task.FromResult(data.ToArray());
    }

    private static void DrawSingleSpeaker(SKCanvas canvas, EventSpeakerSession? session, float x, float y, float w, float h, SKColor accentColor)
    {
        var speaker = session?.Speaker;
        var speakerName = speaker?.Name ?? "Guest Speaker";
        var talkTitle = session?.TalkTitle ?? "Exciting Technical Session";
        var talkDesc = session?.TalkDescription ?? speaker?.Bio ?? string.Empty;

        // Card Container
        var cardRect = new SKRoundRect(new SKRect(x, y, x + w, y + h), 16, 16);
        using (var cardPaint = new SKPaint { Color = new SKColor(32, 35, 50, 220), IsAntialias = true })
        {
            canvas.DrawRoundRect(cardRect, cardPaint);
        }
        using (var cardBorder = new SKPaint { Color = accentColor.WithAlpha(80), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
        {
            canvas.DrawRoundRect(cardRect, cardBorder);
        }

        // Avatar circle
        const float avatarRadius = 90;
        var avatarCenterX = x + 130;
        var avatarCenterY = y + h / 2 - 20;

        using (var avatarBg = new SKPaint { Color = accentColor, IsAntialias = true })
        {
            canvas.DrawCircle(avatarCenterX, avatarCenterY, avatarRadius + 4, avatarBg);
        }

        SKBitmap? photoBitmap = LoadSpeakerBitmap(speaker?.PhotoBlobUrl);
        if (photoBitmap != null)
        {
            using (photoBitmap)
            using (var circlePath = new SKPath())
            {
                circlePath.AddCircle(avatarCenterX, avatarCenterY, avatarRadius);
                canvas.Save();
                canvas.ClipPath(circlePath, antialias: true);
                var destRect = new SKRect(avatarCenterX - avatarRadius, avatarCenterY - avatarRadius, avatarCenterX + avatarRadius, avatarCenterY + avatarRadius);
                canvas.DrawBitmap(photoBitmap, destRect);
                canvas.Restore();
            }
        }
        else
        {
            using (var placeholderPaint = new SKPaint { Color = new SKColor(45, 55, 72), IsAntialias = true })
            {
                canvas.DrawCircle(avatarCenterX, avatarCenterY, avatarRadius, placeholderPaint);
            }
            var initials = string.Concat(speakerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s[0]));
            if (string.IsNullOrEmpty(initials)) initials = "SP";
            using (var fontInit = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 50))
            using (var initPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                var iw = fontInit.MeasureText(initials, initPaint);
                canvas.DrawText(initials, avatarCenterX - iw / 2, avatarCenterY + 18, SKTextAlign.Left, fontInit, initPaint);
            }
        }

        // Speaker Name & Session details
        var contentX = x + 260;
        using (var fontName = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 36))
        using (var namePaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(speakerName, contentX, y + 65, SKTextAlign.Left, fontName, namePaint);
        }

        if (!string.IsNullOrWhiteSpace(speaker?.TwitterHandle) || !string.IsNullOrWhiteSpace(speaker?.BlueSkyHandle))
        {
            var handleText = speaker.BlueSkyHandle ?? speaker.TwitterHandle ?? string.Empty;
            using var fontHandle = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 20);
            using var handlePaint = new SKPaint { Color = new SKColor(147, 197, 253), IsAntialias = true };
            canvas.DrawText(handleText, contentX, y + 100, SKTextAlign.Left, fontHandle, handlePaint);
        }

        // Talk Title
        using (var fontTitle = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 30))
        using (var titlePaint = new SKPaint { Color = new SKColor(253, 224, 71), IsAntialias = true })
        {
            var truncatedTitle = talkTitle.Length > 55 ? talkTitle[..52] + "..." : talkTitle;
            canvas.DrawText(truncatedTitle, contentX, y + 155, SKTextAlign.Left, fontTitle, titlePaint);
        }

        // Description
        if (!string.IsNullOrWhiteSpace(talkDesc))
        {
            var desc = talkDesc.Length > 160 ? talkDesc[..157] + "..." : talkDesc;
            using var fontDesc = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 20);
            using var descPaint = new SKPaint { Color = new SKColor(209, 213, 219), IsAntialias = true };
            canvas.DrawText(desc, contentX, y + 205, SKTextAlign.Left, fontDesc, descPaint);
        }
    }

    private static void DrawSpeakerCard(SKCanvas canvas, EventSpeakerSession session, float x, float y, float w, float h, SKColor accentColor, int index)
    {
        var speaker = session.Speaker;
        var speakerName = speaker?.Name ?? $"Speaker {index}";
        var talkTitle = session.TalkTitle;

        var cardRect = new SKRoundRect(new SKRect(x, y, x + w, y + h), 16, 16);
        using (var cardPaint = new SKPaint { Color = new SKColor(32, 35, 50, 220), IsAntialias = true })
        {
            canvas.DrawRoundRect(cardRect, cardPaint);
        }
        using (var cardBorder = new SKPaint { Color = accentColor.WithAlpha(80), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
        {
            canvas.DrawRoundRect(cardRect, cardBorder);
        }

        const float avatarRadius = 65;
        var avatarCenterX = x + 90;
        var avatarCenterY = y + 90;

        using (var avatarBg = new SKPaint { Color = accentColor, IsAntialias = true })
        {
            canvas.DrawCircle(avatarCenterX, avatarCenterY, avatarRadius + 3, avatarBg);
        }

        SKBitmap? photoBitmap = LoadSpeakerBitmap(speaker?.PhotoBlobUrl);
        if (photoBitmap != null)
        {
            using (photoBitmap)
            using (var circlePath = new SKPath())
            {
                circlePath.AddCircle(avatarCenterX, avatarCenterY, avatarRadius);
                canvas.Save();
                canvas.ClipPath(circlePath, antialias: true);
                var destRect = new SKRect(avatarCenterX - avatarRadius, avatarCenterY - avatarRadius, avatarCenterX + avatarRadius, avatarCenterY + avatarRadius);
                canvas.DrawBitmap(photoBitmap, destRect);
                canvas.Restore();
            }
        }
        else
        {
            using var phPaint = new SKPaint { Color = new SKColor(45, 55, 72), IsAntialias = true };
            canvas.DrawCircle(avatarCenterX, avatarCenterY, avatarRadius, phPaint);
            var initials = string.Concat(speakerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s[0]));
            if (string.IsNullOrEmpty(initials)) initials = $"S{index}";
            using var fontInit = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 36);
            using var initPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var iw = fontInit.MeasureText(initials, initPaint);
            canvas.DrawText(initials, avatarCenterX - iw / 2, avatarCenterY + 12, SKTextAlign.Left, fontInit, initPaint);
        }

        // Speaker Name
        var textLeft = x + 180;
        using (var fontName = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 28))
        using (var namePaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(speakerName, textLeft, y + 80, SKTextAlign.Left, fontName, namePaint);
        }

        // Social / Title
        var handle = speaker?.BlueSkyHandle ?? speaker?.TwitterHandle ?? string.Empty;
        if (!string.IsNullOrEmpty(handle))
        {
            using var fontH = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 18);
            using var hPaint = new SKPaint { Color = new SKColor(147, 197, 253), IsAntialias = true };
            canvas.DrawText(handle, textLeft, y + 110, SKTextAlign.Left, fontH, hPaint);
        }

        // Talk Title
        using (var fontTitle = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 24))
        using (var titlePaint = new SKPaint { Color = new SKColor(253, 224, 71), IsAntialias = true })
        {
            var truncated = talkTitle.Length > 40 ? talkTitle[..37] + "..." : talkTitle;
            canvas.DrawText(truncated, x + 30, y + 210, SKTextAlign.Left, fontTitle, titlePaint);
        }

        // Talk Description
        if (!string.IsNullOrWhiteSpace(session.TalkDescription))
        {
            var desc = session.TalkDescription.Length > 90 ? session.TalkDescription[..87] + "..." : session.TalkDescription;
            using var fontDesc = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 18);
            using var descPaint = new SKPaint { Color = new SKColor(209, 213, 219), IsAntialias = true };
            canvas.DrawText(desc, x + 30, y + 250, SKTextAlign.Left, fontDesc, descPaint);
        }
    }

    private static SKBitmap? LoadSpeakerBitmap(string? photoUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(photoUrl))
            {
                var samplePath = Path.Combine(Directory.GetCurrentDirectory(), "images", "speakers", "Luce Carter.jpg");
                if (File.Exists(samplePath))
                {
                    return SKBitmap.Decode(samplePath);
                }
                return null;
            }

            if (File.Exists(photoUrl))
            {
                return SKBitmap.Decode(photoUrl);
            }

            var localRel = photoUrl.TrimStart('/', '\\');
            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", localRel);
            if (File.Exists(localPath))
            {
                return SKBitmap.Decode(localPath);
            }
        }
        catch
        {
            // Fallback to initials placeholder on decode error
        }

        return null;
    }
}
