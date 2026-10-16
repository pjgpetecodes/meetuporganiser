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

    // The group's uploaded deck is used when it exists; any failure falls back to the built-in decks.
    private async Task<byte[]?> TryLoadGroupTemplateAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken)
    {
        if (_blobStorage == null || !PptxTemplateStore.TryParseFileName(meetupEvent.MeetupGroup?.PptxTemplateBlobUrl, out var fileName)) return null;

        return await PptxTemplateStore.LoadAsync(_blobStorage, fileName, _logger, cancellationToken);
    }

    // Slides containing {{tokens}} are filled; slides for the other speaker count are hidden; the rest are untouched.
    private async Task<byte[]?> GenerateFromGroupTemplateAsync(MeetupEvent meetupEvent, MeetupEvent? nextEvent, byte[] templateBytes, int speakerCount, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        memoryStream.Write(templateBytes);
        memoryStream.Position = 0;

        var currentSessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var nextSessions = nextEvent?.Sessions.OrderBy(s => s.OrderIndex).ToList() ?? [];
        var nextCount = nextSessions.Count;
        IReadOnlyList<byte[]> currentPhotos =
        [
            await PptxSpeakerPhotos.LoadAsync(currentSessions.ElementAtOrDefault(0)?.Speaker, cancellationToken),
            await PptxSpeakerPhotos.LoadAsync(currentSessions.ElementAtOrDefault(1)?.Speaker, cancellationToken)
        ];
        IReadOnlyList<byte[]> nextPhotos = nextEvent == null ? [] :
        [
            await PptxSpeakerPhotos.LoadAsync(nextSessions.ElementAtOrDefault(0)?.Speaker, cancellationToken),
            await PptxSpeakerPhotos.LoadAsync(nextSessions.ElementAtOrDefault(1)?.Speaker, cancellationToken)
        ];

        using (var presentationDoc = PresentationDocument.Open(memoryStream, true))
        {
            var presentationPart = presentationDoc.PresentationPart;
            var slideParts = (presentationPart?.Presentation.SlideIdList?.Elements<SlideId>() ?? [])
                .Select(id => (SlidePart)presentationPart!.GetPartById(id.RelationshipId!))
                .Where(p => p.Slide != null)
                .Select(p =>
                {
                    var tokens = PptxTokens.DetectTokens(p.Slide!);
                    var nextOnly = PptxTokens.UsesOnlyNextEventTokens(tokens);
                    return (Part: p, Kind: nextOnly ? PptxTokens.ClassifyNextEvent(tokens) : PptxTokens.Classify(tokens), HasTokens: tokens.Count > 0, NextOnly: nextOnly);
                })
                .ToList();

            if (!slideParts.Any(s => s.HasTokens))
            {
                _logger.LogWarning("The group's PPTX template has no {{tokens}} on any slide; using the built-in deck.");
                return null;
            }

            var replacements = PptxTokens.BuildReplacements(meetupEvent, includeSampleLiterals: false, nextEvent);
            var socialSection = PptxTokens.FindSocialSection(slideParts.Select(s => (PptxTokens.TextOf(s.Part.Slide!), (IReadOnlyList<string>)PptxTokens.DetectTokens(s.Part.Slide!))).ToList());
            var hasTwoSpeakerSlides = slideParts.Any(s => !s.NextOnly && s.Kind == PptxTokens.SlideKind.TwoSpeakers);
            var hasTwoNextSlides = slideParts.Any(s => s.NextOnly && s.Kind == PptxTokens.SlideKind.TwoSpeakers);
            var slideIndex = -1;
            foreach (var (part, kind, hasTokens, nextOnly) in slideParts)
            {
                slideIndex++;
                if (hasTokens) PptxTokens.ApplyToSlide(part.Slide!, replacements);

                var count = nextOnly ? nextCount : speakerCount;
                var twoSlidesExist = nextOnly ? hasTwoNextSlides : hasTwoSpeakerSlides;
                var hide = count > 1
                    ? kind == PptxTokens.SlideKind.OneSpeaker && twoSlidesExist
                    : kind == PptxTokens.SlideKind.TwoSpeakers;
                if (nextEvent == null && nextOnly) hide = true;
                if (socialSection.Contains(slideIndex)) hide = true;
                // Keep slides the template author already hid; only add hiding for the other speaker count.
                if (hide) part.Slide!.Show = false;
                else if (nextOnly) part.Slide!.Show = null;

                if (hasTokens) PptxSpeakerPhotos.Apply(part, kind, nextOnly ? nextPhotos : currentPhotos);
                part.Slide!.Save();

                foreach (var imagePart in part.ImageParts.Where(i => i.ContentType is "image/jpeg" or "image/png").Distinct())
                {
                    NormalizeImagePart(imagePart);
                }
            }

            presentationPart!.Presentation.Save();
        }

        return memoryStream.ToArray();
    }

    public async Task<byte[]> GenerateEventDeckAsync(MeetupEvent meetupEvent, MeetupEvent? nextEvent = null, CancellationToken cancellationToken = default)
    {
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();

        if (await TryLoadGroupTemplateAsync(meetupEvent, cancellationToken) is { } custom)
        {
            var deck = await GenerateFromGroupTemplateAsync(meetupEvent, nextEvent, custom, sessions.Count, cancellationToken);
            if (deck != null) return deck;
        }

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

        byte[]? templateBytes = null;
        if (File.Exists(templatePath))
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
                var speaker2 = sessions.Skip(1).FirstOrDefault()?.Speaker;
                var replacements = PptxTokens.BuildReplacements(meetupEvent, includeSampleLiterals: true, nextEvent);
                var photos = new[]
                {
                    await PptxSpeakerPhotos.LoadAsync(speaker1, cancellationToken),
                    await PptxSpeakerPhotos.LoadAsync(speaker2, cancellationToken)
                };

                // Iterate through all slide parts and replace text occurrences
                foreach (var slideId in presentationPart.Presentation.SlideIdList?.Elements<SlideId>() ?? [])
                {
                    if (slideId.RelationshipId == null) continue;
                    var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId!);
                    if (slidePart?.Slide == null) continue;

                    var kind = PptxTokens.Classify(PptxTokens.DetectTokens(slidePart.Slide));

                    if (PptxTokens.IsUpcomingEventSlide(slidePart.Slide))
                    {
                        if (nextEvent != null)
                        {
                            PptxTokens.ApplyUpcomingEventSlide(slidePart.Slide, nextEvent);
                            slidePart.Slide.Show = null;
                        }
                        slidePart.Slide.Save();
                        continue;
                    }

                    if (PptxTokens.IsUpcomingTitleSlide(slidePart.Slide) && nextEvent != null) slidePart.Slide.Show = null;
                    PptxTokens.ApplyToSlide(slidePart.Slide, replacements);
                    PptxSpeakerPhotos.Apply(slidePart, kind, photos);
                    slidePart.Slide.Save();
                }

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

    private static void NormalizeImagePart(ImagePart imagePart)
    {
        try
        {
            byte[] original;
            using (var read = imagePart.GetStream(FileMode.Open, FileAccess.Read))
            using (var ms = new MemoryStream())
            {
                read.CopyTo(ms);
                original = ms.ToArray();
            }

            var upright = ImageOrientation.Normalize(original);
            if (ReferenceEquals(upright, original)) return;

            using var write = imagePart.GetStream(FileMode.Create, FileAccess.Write);
            write.Write(upright, 0, upright.Length);
        }
        catch
        {
            // Leave images we cannot decode untouched.
        }
    }

    internal static byte[]? LoadSpeakerBytes(string? photoUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(photoUrl))
            {
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
