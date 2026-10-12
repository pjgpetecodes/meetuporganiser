using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class PptxGeneratorService : IPptxGeneratorService
{
    private readonly ILogger<PptxGeneratorService> _logger;

    public PptxGeneratorService(ILogger<PptxGeneratorService> logger)
    {
        _logger = logger;
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
                var candidate = Directory.GetFiles(Directory.GetCurrentDirectory(), templateFileName, SearchOption.AllDirectories).FirstOrDefault();
                if (candidate != null) templatePath = candidate;
            }
        }

        byte[] templateBytes;
        if (File.Exists(templatePath))
        {
            templateBytes = await File.ReadAllBytesAsync(templatePath, cancellationToken);
        }
        else
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

                var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Luce Carter", speaker1?.Name ?? "Speaker Name" },
                    { "SQL to MongoDB API with C#", session1?.TalkTitle ?? meetupEvent.Title },
                    { "https://bit.ly/dotnetnottsapril2026", meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "https://meetup.com" },
                    { "bit.ly/dotnetnottsapril2026", (meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "meetup.com").Replace("https://", "").Replace("http://", "") },
                    { "Monday 27th April 2026", meetupEvent.EventDateTimeUtc.ToString("dddd d'th' MMMM yyyy") },
                    { "Monday", meetupEvent.EventDateTimeUtc.ToString("dddd") },
                    { "April 2026", meetupEvent.EventDateTimeUtc.ToString("MMMM yyyy") }
                };

                if (speaker2 != null && session2 != null)
                {
                    replacements["Pete Gallagher"] = speaker2.Name;
                    replacements["Detecting Pollinating Insects with a Raspberry Pi and the Hailo AI Kit"] = session2.TalkTitle;
                    replacements["Christophe Crémon"] = speaker1?.Name ?? "Speaker 1";
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

                presentationPart.Presentation.Save();
            }
        }

        return memoryStream.ToArray();
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
