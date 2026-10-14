using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MeetupOrganiser.Infrastructure.Services;

public record PptxSlideInfo(int Number, string Caption, IReadOnlyList<string> Tokens, PptxTokens.SlideKind Kind);

public interface IPptxPreviewService
{
    Task<IReadOnlyList<PptxSlideInfo>> GetSlidesAsync(string fileName, CancellationToken cancellationToken = default);

    Task<byte[]?> RenderSlideAsync(string fileName, int slideNumber, int width, CancellationToken cancellationToken = default);

    Task<byte[]?> RenderFilledSlideAsync(string fileName, int slideNumber, int speakerCount, int width, CancellationToken cancellationToken = default);
}

// Approximate renderer: backgrounds, pictures, filled shapes and text boxes. Anything else is skipped.
public class PptxPreviewService : IPptxPreviewService
{
    private const double EmuPerPoint = 12700;
    private readonly IBlobStorageService _blobStorage;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PptxPreviewService> _logger;
    private static readonly SemaphoreSlim _gate = new(2);

    public PptxPreviewService(IBlobStorageService blobStorage, IMemoryCache cache, ILogger<PptxPreviewService> logger)
    {
        _blobStorage = blobStorage;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PptxSlideInfo>> GetSlidesAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var deck = await GetDeckAsync(fileName, cancellationToken);
        if (deck == null) return [];

        using var stream = new MemoryStream(deck, false);
        using var doc = PresentationDocument.Open(stream, false);
        var result = new List<PptxSlideInfo>();
        var number = 0;
        foreach (var part in SlideParts(doc))
        {
            number++;
            var caption = part.Slide?.Descendants<D.Paragraph>()
                .Select(p => string.Concat(p.Descendants<D.Text>().Select(t => t.Text)).Trim())
                .FirstOrDefault(t => t.Length > 0) ?? "(no text)";
            var tokens = part.Slide == null ? [] : PptxTokens.DetectTokens(part.Slide);
            result.Add(new PptxSlideInfo(number, caption.Length > 60 ? caption[..60] + "..." : caption, tokens, PptxTokens.Classify(tokens)));
        }

        return result;
    }

    public async Task<byte[]?> RenderSlideAsync(string fileName, int slideNumber, int width, CancellationToken cancellationToken = default)
    {
        var key = $"pptx-thumb:{fileName}:{slideNumber}:{width}";
        if (_cache.TryGetValue(key, out byte[]? cached)) return cached;

        var deck = await GetDeckAsync(fileName, cancellationToken);
        var png = deck == null ? null : await RenderAsync(deck, slideNumber, width, null, cancellationToken);
        if (png != null) _cache.Set(key, png, TimeSpan.FromMinutes(30));
        return png;
    }

    public async Task<byte[]?> RenderFilledSlideAsync(string fileName, int slideNumber, int speakerCount, int width, CancellationToken cancellationToken = default)
    {
        var deck = await GetDeckAsync(fileName, cancellationToken);
        return deck == null ? null : await RenderAsync(deck, slideNumber, width, SampleEvent(speakerCount), cancellationToken);
    }

    public static MeetupEvent SampleEvent(int speakerCount)
    {
        var date = DateTime.UtcNow.AddMonths(1);
        var evt = new MeetupEvent
        {
            Title = "Sample Meetup",
            EventDateTimeUtc = new DateTime(date.Year, date.Month, 15, 18, 30, 0, DateTimeKind.Utc),
            LocationOrPlatform = "CGI Notts + Zoom",
            Format = EventFormat.Hybrid,
            ShortBitlyUrl = "https://bit.ly/example",
        };
        evt.Sessions.Add(new EventSpeakerSession { OrderIndex = 0, TalkTitle = "Sample Talk Title", Speaker = new Speaker { Name = "Luce Carter" } });
        if (speakerCount > 1)
        {
            evt.Sessions.Add(new EventSpeakerSession { OrderIndex = 1, TalkTitle = "Another Sample Talk", Speaker = new Speaker { Name = "Pete Gallagher" } });
        }

        return evt;
    }

    private async Task<byte[]?> GetDeckAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!PptxTemplateStore.IsValidFileName(fileName)) return null;

        var key = $"pptx-deck:{fileName}";
        if (_cache.TryGetValue(key, out byte[]? cached)) return cached;

        var bytes = await PptxTemplateStore.LoadAsync(_blobStorage, fileName, _logger, cancellationToken);
        if (bytes != null) _cache.Set(key, bytes, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10), Size = null });
        return bytes;
    }

    private static IEnumerable<SlidePart> SlideParts(PresentationDocument doc)
    {
        var presentationPart = doc.PresentationPart;
        foreach (var id in presentationPart?.Presentation.SlideIdList?.Elements<SlideId>() ?? [])
        {
            if (id.RelationshipId?.Value is { } rid) yield return (SlidePart)presentationPart!.GetPartById(rid);
        }
    }

    private async Task<byte[]?> RenderAsync(byte[] deck, int slideNumber, int width, MeetupEvent? sample, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var stream = new MemoryStream();
            stream.Write(deck);
            stream.Position = 0;
            using var doc = PresentationDocument.Open(stream, true);
            var parts = SlideParts(doc).ToList();
            if (slideNumber < 1 || slideNumber > parts.Count) return null;

            var part = parts[slideNumber - 1];
            if (sample != null && part.Slide != null)
            {
                PptxTokens.ApplyToSlide(part.Slide, PptxTokens.BuildReplacements(sample, includeSampleLiterals: false));
            }

            var size = doc.PresentationPart!.Presentation.SlideSize;
            double cx = size?.Cx?.Value ?? 12192000, cy = size?.Cy?.Value ?? 6858000;
            var height = (int)Math.Round(width * cy / cx);

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.Scale((float)(width / cx));

            var theme = doc.PresentationPart.ThemePart?.Theme;
            var ctx = new RenderContext(canvas, theme, cx, cy);
            DrawBackground(ctx, part);
            if (part.SlideLayoutPart?.SlideLayout?.CommonSlideData?.ShapeTree is { } layoutTree)
            {
                DrawTree(ctx, layoutTree, part.SlideLayoutPart, skipPlaceholders: true);
            }

            if (part.Slide?.CommonSlideData?.ShapeTree is { } tree) DrawTree(ctx, tree, part, skipPlaceholders: false);

            using var image = SKImage.FromBitmap(bitmap);
            return image.Encode(SKEncodedImageFormat.Png, 90).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not render slide {Slide} preview.", slideNumber);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record RenderContext(SKCanvas Canvas, D.Theme? Theme, double Cx, double Cy);

    private static void DrawBackground(RenderContext ctx, SlidePart part)
    {
        var background = part.Slide?.CommonSlideData?.Background
            ?? part.SlideLayoutPart?.SlideLayout?.CommonSlideData?.Background
            ?? part.SlideLayoutPart?.SlideMasterPart?.SlideMaster?.CommonSlideData?.Background;
        var props = background?.BackgroundProperties;
        if (props == null) return;

        var owner = part.Slide?.CommonSlideData?.Background != null ? (OpenXmlPart)part : part.SlideLayoutPart!;
        var rect = new SKRect(0, 0, (float)ctx.Cx, (float)ctx.Cy);
        if (props.GetFirstChild<D.BlipFill>() is { } blip)
        {
            DrawBlip(ctx, owner, blip, rect);
            return;
        }

        using var paint = FillPaint(ctx, props, rect);
        if (paint != null) ctx.Canvas.DrawRect(rect, paint);
    }

    private static void DrawTree(RenderContext ctx, OpenXmlElement tree, OpenXmlPart owner, bool skipPlaceholders)
    {
        foreach (var child in tree.ChildElements)
        {
            switch (child)
            {
                case Picture pic:
                    DrawPicture(ctx, pic, owner);
                    break;
                case Shape shape:
                    if (skipPlaceholders && shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.GetFirstChild<PlaceholderShape>() != null) break;
                    DrawShape(ctx, shape, owner);
                    break;
                case GroupShape group:
                    DrawGroup(ctx, group, owner, skipPlaceholders);
                    break;
            }
        }
    }

    private static void DrawGroup(RenderContext ctx, GroupShape group, OpenXmlPart owner, bool skipPlaceholders)
    {
        var xfrm = group.GroupShapeProperties?.TransformGroup;
        if (xfrm?.Offset == null || xfrm.Extents == null || xfrm.ChildOffset == null || xfrm.ChildExtents == null)
        {
            DrawTree(ctx, group, owner, skipPlaceholders);
            return;
        }

        double chCx = xfrm.ChildExtents.Cx!.Value, chCy = xfrm.ChildExtents.Cy!.Value;
        if (chCx == 0 || chCy == 0) return;

        ctx.Canvas.Save();
        ctx.Canvas.Translate(xfrm.Offset.X!.Value, xfrm.Offset.Y!.Value);
        ctx.Canvas.Scale((float)(xfrm.Extents.Cx!.Value / chCx), (float)(xfrm.Extents.Cy!.Value / chCy));
        ctx.Canvas.Translate(-xfrm.ChildOffset.X!.Value, -xfrm.ChildOffset.Y!.Value);
        DrawTree(ctx, group, owner, skipPlaceholders);
        ctx.Canvas.Restore();
    }

    private static SKRect? Bounds(D.Transform2D? xfrm) =>
        xfrm?.Offset == null || xfrm.Extents == null
            ? null
            : SKRect.Create(xfrm.Offset.X!.Value, xfrm.Offset.Y!.Value, xfrm.Extents.Cx!.Value, xfrm.Extents.Cy!.Value);

    private static void DrawPicture(RenderContext ctx, Picture pic, OpenXmlPart owner)
    {
        if (Bounds(pic.ShapeProperties?.Transform2D) is not { } rect || pic.BlipFill == null) return;

        WithRotation(ctx, pic.ShapeProperties!.Transform2D!, rect, () => DrawBlip(ctx, owner, pic.BlipFill, rect));
    }

    private static void WithRotation(RenderContext ctx, D.Transform2D xfrm, SKRect rect, Action draw)
    {
        ctx.Canvas.Save();
        if (xfrm.Rotation?.Value is { } rot and not 0) ctx.Canvas.RotateDegrees(rot / 60000f, rect.MidX, rect.MidY);
        draw();
        ctx.Canvas.Restore();
    }

    private static void DrawBlip(RenderContext ctx, OpenXmlPart owner, OpenXmlElement blipFill, SKRect rect)
    {
        var embed = blipFill.GetFirstChild<D.Blip>()?.Embed?.Value;
        if (embed == null) return;

        try
        {
            using var imageStream = owner.GetPartById(embed).GetStream();
            using var bitmap = SKBitmap.Decode(imageStream);
            if (bitmap == null) return;

            var src = new SKRect(0, 0, bitmap.Width, bitmap.Height);
            if (blipFill.GetFirstChild<D.SourceRectangle>() is { } crop)
            {
                float l = (crop.Left?.Value ?? 0) / 100000f, t = (crop.Top?.Value ?? 0) / 100000f;
                float r = (crop.Right?.Value ?? 0) / 100000f, b = (crop.Bottom?.Value ?? 0) / 100000f;
                src = new SKRect(bitmap.Width * l, bitmap.Height * t, bitmap.Width * (1 - r), bitmap.Height * (1 - b));
            }

            using var paint = new SKPaint { IsAntialias = true };
            ctx.Canvas.DrawBitmap(bitmap, src, rect, paint);
        }
        catch
        {
            // Unsupported image formats (e.g. EMF) are skipped.
        }
    }

    private static void DrawShape(RenderContext ctx, Shape shape, OpenXmlPart owner)
    {
        var xfrm = shape.ShapeProperties?.Transform2D;
        if (Bounds(xfrm) is not { } rect) return;

        WithRotation(ctx, xfrm!, rect, () =>
        {
            var props = shape.ShapeProperties!;
            if (props.GetFirstChild<D.BlipFill>() is { } blip)
            {
                DrawBlip(ctx, owner, blip, rect);
            }
            else if (FillPaint(ctx, props, rect) is { } fill)
            {
                using (fill) DrawGeometry(ctx.Canvas, props, rect, fill);
            }

            if (shape.TextBody != null) DrawText(ctx, shape.TextBody, rect);
        });
    }

    private static void DrawGeometry(SKCanvas canvas, OpenXmlElement props, SKRect rect, SKPaint paint)
    {
        var preset = props.GetFirstChild<D.PresetGeometry>()?.Preset?.Value;
        if (preset == D.ShapeTypeValues.Ellipse) canvas.DrawOval(rect, paint);
        else if (preset == D.ShapeTypeValues.RoundRectangle)
        {
            var radius = Math.Min(rect.Width, rect.Height) / 6;
            canvas.DrawRoundRect(rect, radius, radius, paint);
        }
        else canvas.DrawRect(rect, paint);
    }

    private static SKPaint? FillPaint(RenderContext ctx, OpenXmlElement props, SKRect rect)
    {
        if (props.GetFirstChild<D.SolidFill>() is { } solid && ResolveColor(ctx, solid) is { } colour)
        {
            return new SKPaint { Color = colour, IsAntialias = true, Style = SKPaintStyle.Fill };
        }

        if (props.GetFirstChild<D.GradientFill>() is { } gradient)
        {
            var stops = gradient.GradientStopList?.Elements<D.GradientStop>()
                .Select(s => (Pos: (s.Position?.Value ?? 0) / 100000f, Colour: ResolveColor(ctx, s)))
                .Where(s => s.Colour != null).ToList() ?? [];
            if (stops.Count == 0) return null;
            if (stops.Count == 1) return new SKPaint { Color = stops[0].Colour!.Value, IsAntialias = true };

            var angle = (gradient.GetFirstChild<D.LinearGradientFill>()?.Angle?.Value ?? 0) / 60000.0 * Math.PI / 180;
            var dx = (float)Math.Cos(angle) * rect.Width / 2;
            var dy = (float)Math.Sin(angle) * rect.Height / 2;
            var shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.MidX - dx, rect.MidY - dy), new SKPoint(rect.MidX + dx, rect.MidY + dy),
                stops.Select(s => s.Colour!.Value).ToArray(), stops.Select(s => s.Pos).ToArray(), SKShaderTileMode.Clamp);
            return new SKPaint { Shader = shader, IsAntialias = true };
        }

        return null;
    }

    private static SKColor? ResolveColor(RenderContext ctx, OpenXmlElement holder)
    {
        SKColor? colour = null;
        if (holder.GetFirstChild<D.RgbColorModelHex>() is { Val.Value: { } hex } && SKColor.TryParse(hex, out var parsed))
        {
            colour = parsed;
            if (holder.GetFirstChild<D.RgbColorModelHex>()!.GetFirstChild<D.Alpha>()?.Val?.Value is { } a)
            {
                colour = colour.Value.WithAlpha((byte)(a * 255 / 100000));
            }
        }
        else if (holder.GetFirstChild<D.SchemeColor>() is { } scheme && scheme.Val?.Value is { } name)
        {
            colour = SchemeColour(ctx.Theme, name);
            if (colour != null && scheme.GetFirstChild<D.Alpha>()?.Val?.Value is { } a)
            {
                colour = colour.Value.WithAlpha((byte)(a * 255 / 100000));
            }
        }

        return colour;
    }

    private static SKColor? SchemeColour(D.Theme? theme, D.SchemeColorValues name)
    {
        var scheme = theme?.ThemeElements?.ColorScheme;
        if (scheme == null) return null;

        D.Color2Type? slot = null;
        if (name == D.SchemeColorValues.Background1 || name == D.SchemeColorValues.Light1) slot = scheme.Light1Color;
        else if (name == D.SchemeColorValues.Text1 || name == D.SchemeColorValues.Dark1) slot = scheme.Dark1Color;
        else if (name == D.SchemeColorValues.Background2 || name == D.SchemeColorValues.Light2) slot = scheme.Light2Color;
        else if (name == D.SchemeColorValues.Text2 || name == D.SchemeColorValues.Dark2) slot = scheme.Dark2Color;
        else if (name == D.SchemeColorValues.Accent1) slot = scheme.Accent1Color;
        else if (name == D.SchemeColorValues.Accent2) slot = scheme.Accent2Color;
        else if (name == D.SchemeColorValues.Accent3) slot = scheme.Accent3Color;
        else if (name == D.SchemeColorValues.Accent4) slot = scheme.Accent4Color;
        else if (name == D.SchemeColorValues.Accent5) slot = scheme.Accent5Color;
        else if (name == D.SchemeColorValues.Accent6) slot = scheme.Accent6Color;

        var hex = slot?.RgbColorModelHex?.Val?.Value ?? slot?.SystemColor?.LastColor?.Value;
        return hex != null && SKColor.TryParse(hex, out var c) ? c : null;
    }

    private static void DrawText(RenderContext ctx, TextBody body, SKRect rect)
    {
        var props = body.BodyProperties;
        float left = (props?.LeftInset?.Value ?? 91440), right = (props?.RightInset?.Value ?? 91440);
        float top = (props?.TopInset?.Value ?? 45720), bottom = (props?.BottomInset?.Value ?? 45720);
        var area = new SKRect(rect.Left + left, rect.Top + top, rect.Right - right, rect.Bottom - bottom);
        if (area.Width <= 0 || area.Height <= 0) return;

        var lines = new List<(List<(string Word, SKFont Font, SKPaint Paint)> Words, float Height, D.TextAlignmentTypeValues Align)>();
        foreach (var paragraph in body.Elements<D.Paragraph>())
        {
            var align = paragraph.ParagraphProperties?.Alignment?.Value ?? D.TextAlignmentTypeValues.Left;
            var words = new List<(string, SKFont, SKPaint)>();
            float maxSize = 0;
            foreach (var run in paragraph.Elements<D.Run>())
            {
                var rp = run.RunProperties;
                var size = (rp?.FontSize?.Value ?? 1800) / 100f * (float)EmuPerPoint;
                maxSize = Math.Max(maxSize, size);
                var style = new SKFontStyle(rp?.Bold?.Value == true ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal, rp?.Italic?.Value == true ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                var typeface = SKTypeface.FromFamilyName(rp?.GetFirstChild<D.LatinFont>()?.Typeface?.Value is { Length: > 0 } f && !f.StartsWith('+') ? f : "Segoe UI", style);
                var font = new SKFont(typeface, size);
                var paint = new SKPaint { IsAntialias = true, Color = (rp != null ? ResolveColor(ctx, rp) : null) ?? SKColors.Black };
                foreach (var word in (run.Text?.Text ?? string.Empty).Split(' ')) words.Add((word, font, paint));
            }

            if (maxSize == 0) maxSize = (paragraph.GetFirstChild<D.EndParagraphRunProperties>()?.FontSize?.Value ?? 1800) / 100f * (float)EmuPerPoint;
            lines.AddRange(Wrap(words, area.Width, align, maxSize));
        }

        var total = lines.Sum(l => l.Height);
        var anchor = props?.Anchor?.Value;
        var y = area.Top;
        if (anchor == D.TextAnchoringTypeValues.Center) y = area.MidY - total / 2;
        else if (anchor == D.TextAnchoringTypeValues.Bottom) y = area.Bottom - total;

        foreach (var (words, height, align) in lines)
        {
            var lineWidth = words.Sum((w, i) => w.Font.MeasureText(w.Word + (i < words.Count - 1 ? " " : string.Empty)));
            var x = align == D.TextAlignmentTypeValues.Center ? area.MidX - lineWidth / 2
                : align == D.TextAlignmentTypeValues.Right ? area.Right - lineWidth : area.Left;
            var baseline = y + height * 0.8f;
            for (var i = 0; i < words.Count; i++)
            {
                var text = words[i].Word + (i < words.Count - 1 ? " " : string.Empty);
                ctx.Canvas.DrawText(text, x, baseline, SKTextAlign.Left, words[i].Font, words[i].Paint);
                x += words[i].Font.MeasureText(text);
            }

            y += height;
        }
    }

    private static IEnumerable<(List<(string, SKFont, SKPaint)>, float, D.TextAlignmentTypeValues)> Wrap(
        List<(string Word, SKFont Font, SKPaint Paint)> words, float maxWidth, D.TextAlignmentTypeValues align, float height)
    {
        var line = new List<(string, SKFont, SKPaint)>();
        float width = 0;
        foreach (var w in words)
        {
            var w1 = w.Font.MeasureText(w.Word + " ");
            if (line.Count > 0 && width + w1 > maxWidth)
            {
                yield return (line, height * 1.2f, align);
                line = [];
                width = 0;
            }

            line.Add(w);
            width += w1;
        }

        yield return (line, height * 1.2f, align);
    }
}

internal static class EnumerableExtensions
{
    public static float Sum<T>(this IEnumerable<T> source, Func<T, int, float> selector) =>
        source.Select(selector).Sum();
}
