using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class PptxGeneratorService : IPptxGeneratorService
{
    private readonly ILogger<PptxGeneratorService> _logger;
    private readonly IBlobStorageService? _blobStorage;

    public PptxGeneratorService(ILogger<PptxGeneratorService> logger)
        : this(logger, null)
    {
    }

    public PptxGeneratorService(ILogger<PptxGeneratorService> logger, IBlobStorageService? blobStorage)
    {
        _logger = logger;
        _blobStorage = blobStorage;
    }

    // The group's uploaded template wins; any failure falls back to the built-in decks.
    private async Task<byte[]?> TryLoadGroupTemplateAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken)
    {
        var url = meetupEvent.MeetupGroup?.PptxTemplateBlobUrl;
        if (_blobStorage == null || string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var segments = url.Split('?')[0].TrimEnd('/').Split('/');
            if (segments.Length < 2) return null;

            await using var stream = await _blobStorage.DownloadAsync(
                Uri.UnescapeDataString(segments[^2]), Uri.UnescapeDataString(segments[^1]), cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the group's PPTX template from {Url}; using the built-in deck.", url);
            return null;
        }
    }

    public async Task<byte[]> GenerateEventDeckAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default)
    {
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var templateFileName = sessions.Count > 1 ? "2 Speakers.pptx" : "1 Speaker.pptx";
        var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "pptx", templateFileName);

        if (!File.Exists(templatePath))
        {
            // Search in parent or alternative folders if executing in bin
            templatePath = Path.Combine(AppContext.BaseDirectory, "pptx", templateFileName);
            if (!File.Exists(templatePath))
            {
                var candidate = FindUpwards(templateFileName);
                if (candidate != null) templatePath = candidate;
            }
        }

        var templateBytes = await TryLoadGroupTemplateAsync(meetupEvent, cancellationToken);
        if (templateBytes == null && File.Exists(templatePath))
        {
            templateBytes = await File.ReadAllBytesAsync(templatePath, cancellationToken);
        }

        if (templateBytes == null)
        {
            _logger.LogWarning("PPTX template file {FileName} not found at {Path}. Creating empty deck.", templateFileName, templatePath);
            return CreateEmptyDeck(meetupEvent);
        }

        using var memoryStream = new MemoryStream();
        await memoryStream.WriteAsync(templateBytes, cancellationToken);
        memoryStream.Position = 0;

        using (var presentationDoc = PresentationDocument.Open(memoryStream, true))
        {
            var presentationPart = presentationDoc.PresentationPart;
            if (presentationPart != null)
            {
                var speaker1 = sessions.FirstOrDefault()?.Speaker;
                var session1 = sessions.FirstOrDefault();
                var speaker2 = sessions.Skip(1).FirstOrDefault()?.Speaker;
                var session2 = sessions.Skip(1).FirstOrDefault();

                var dateFormatted = meetupEvent.EventDateTimeUtc.ToString("dddd d'th' MMMM yyyy");
                var dayFormatted = meetupEvent.EventDateTimeUtc.ToString("dddd");
                var monthYearFormatted = meetupEvent.EventDateTimeUtc.ToString("MMMM yyyy");
                var shortUrl = meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "https://meetup.com";
                var shortUrlNoScheme = shortUrl.Replace("https://", "").Replace("http://", "");

                var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Placeholder tokens
                    { "{{SpeakerName}}", speaker1?.Name ?? "Speaker Name" },
                    { "{{Speaker1Name}}", speaker1?.Name ?? "Speaker 1" },
                    { "{{Speaker2Name}}", speaker2?.Name ?? "Speaker 2" },
                    { "{{TalkTitle}}", session1?.TalkTitle ?? meetupEvent.Title },
                    { "{{Talk1Title}}", session1?.TalkTitle ?? meetupEvent.Title },
                    { "{{Talk2Title}}", session2?.TalkTitle ?? "Talk 2" },
                    { "{{ShortUrl}}", shortUrl },
                    { "{{EventDate}}", dateFormatted },
                    { "{{EventTime}}", EventHeader.ToUkTime(meetupEvent.EventDateTimeUtc).ToString("HH:mm") + " UK time" },
                    { "{{Venue}}", meetupEvent.LocationOrPlatform ?? "Online" },
                    { "{{EventDateLine}}", EventHeader.DateLine(meetupEvent) },
                    { "{{EventVenueLine}}", EventHeader.VenueLine(meetupEvent) },
                    { "{{EventFormat}}", EventHeader.FormatLabel(meetupEvent.Format) },
                    { "{{EventSubtitle}}", EventHeader.Subtitle(meetupEvent) ?? string.Empty },

                    // Sample template literal replacements
                    { "Luce Carter", speaker1?.Name ?? "Speaker Name" },
                    { "SQL to MongoDB API with C#", session1?.TalkTitle ?? meetupEvent.Title },
                    { "https://bit.ly/dotnetnottsapril2026", shortUrl },
                    { "bit.ly/dotnetnottsapril2026", shortUrlNoScheme },
                    { "Monday 27th April 2026", dateFormatted },
                    { "Monday", dayFormatted },
                    { "April 2026", monthYearFormatted }
                };

                if (speaker2 != null && session2 != null)
                {
                    replacements["Pete Gallagher"] = speaker2.Name;
                    replacements["Detecting Pollinating Insects with a Raspberry Pi and the Hailo AI Kit"] = session2.TalkTitle;
                    replacements["Christophe Crémon"] = speaker1?.Name ?? "Speaker 1";
                    replacements["Christophe Cr\u00e9mon"] = speaker1?.Name ?? "Speaker 1";
                    replacements["Agents of Change: How Agentic AI is Revolutionizing Every Industry"] = session1?.TalkTitle ?? "Talk 1";
                }

                // Iterate through all slide parts and replace text occurrences
                foreach (var slideId in presentationPart.Presentation.SlideIdList?.Elements<SlideId>() ?? [])
                {
                    if (slideId.RelationshipId == null) continue;
                    var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId!);
                    if (slidePart?.Slide == null) continue;

                    foreach (var textElem in slidePart.Slide.Descendants<D.Text>())
                    {
                        var text = textElem.Text;
                        if (string.IsNullOrEmpty(text)) continue;

                        foreach (var kvp in replacements)
                        {
                            if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                text = text.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
                            }
                        }
                        textElem.Text = text;
                    }

                    slidePart.Slide.Save();
                }

                // Replace speaker photo in image parts if available
                await TryReplaceSpeakerPhotosAsync(presentationPart, speaker1, speaker2);

                presentationPart.Presentation.Save();
            }
        }

        return memoryStream.ToArray();
    }

    private static string? FindUpwards(string relativeFile)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "pptx", relativeFile);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private async Task TryReplaceSpeakerPhotosAsync(PresentationPart presentationPart, Speaker? speaker1, Speaker? speaker2)
    {
        try
        {
            byte[]? photo1Bytes = LoadSpeakerBytes(speaker1?.PhotoBlobUrl) ?? await RemotePhotoLoader.LoadAsync(speaker1?.PhotoBlobUrl);
            byte[]? photo2Bytes = LoadSpeakerBytes(speaker2?.PhotoBlobUrl) ?? await RemotePhotoLoader.LoadAsync(speaker2?.PhotoBlobUrl);

            foreach (var slidePart in presentationPart.SlideParts)
            {
                var uri = slidePart.Uri.ToString();

                // Slide 7 and Slide 9 contain speaker 1 photo (image32.png via rId3)
                if (photo1Bytes != null && (uri.EndsWith("slide7.xml") || uri.EndsWith("slide9.xml")))
                {
                    ReplaceImagePart(slidePart, "rId3", photo1Bytes);
                }

                // Slide 10 contains speaker 1 (rId3 -> image35.png) and speaker 2 (rId2 -> image34.png)
                if (uri.EndsWith("slide10.xml"))
                {
                    if (photo1Bytes != null)
                    {
                        ReplaceImagePart(slidePart, "rId3", photo1Bytes);
                    }
                    if (photo2Bytes != null)
                    {
                        ReplaceImagePart(slidePart, "rId2", photo2Bytes);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to swap speaker headshot images in presentation deck.");
        }
    }

    private static void ReplaceImagePart(SlidePart slidePart, string relId, byte[] newImageBytes)
    {
        try
        {
            if (slidePart.TryGetPartById(relId, out var part) && part is ImagePart imagePart)
            {
                using var stream = imagePart.GetStream(FileMode.Create, FileAccess.Write);
                stream.Write(newImageBytes, 0, newImageBytes.Length);
            }
        }
        catch
        {
            // Ignore if relationship is not found or cannot be overwritten
        }
    }

    private static byte[]? LoadSpeakerBytes(string? photoUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(photoUrl))
            {
                var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "images", "speakers", "Luce Carter.jpg");
                if (File.Exists(defaultPath))
                {
                    return File.ReadAllBytes(defaultPath);
                }
                var altDefault = Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Web", "wwwroot", "images", "speakers", "Luce Carter.jpg");
                if (File.Exists(altDefault))
                {
                    return File.ReadAllBytes(altDefault);
                }
                return null;
            }

            if (File.Exists(photoUrl))
            {
                return File.ReadAllBytes(photoUrl);
            }

            var rel = photoUrl.TrimStart('/', '\\');
            var localCandidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", rel),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Web", "wwwroot", rel),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", rel)
            };

            foreach (var candidate in localCandidates)
            {
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }
        }
        catch
        {
            // Fallback gracefully
        }

        return null;
    }

    private static byte[] CreateEmptyDeck(MeetupEvent evt)
    {
        using var ms = new MemoryStream();
        using (var presentationDoc = PresentationDocument.Create(ms, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var presentationPart = presentationDoc.AddPresentationPart();
            presentationPart.Presentation = new Presentation();
            presentationPart.Presentation.Save();
        }
        return ms.ToArray();
    }
}
