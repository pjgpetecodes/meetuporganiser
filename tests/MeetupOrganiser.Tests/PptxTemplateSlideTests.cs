using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class PptxTemplateSlideTests
{
    private const string FileName = "11111111-1111-1111-1111-111111111111_deck.pptx";

    private sealed class MemoryBlobStorage(byte[] deck) : IBlobStorageService
    {
        public Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult("/storage/" + containerName + "/" + fileName);
        public Task<Stream> DownloadAsync(string containerName, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(deck));
        public Task<bool> DeleteAsync(string containerName, string fileName, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public string GetBlobUrl(string containerName, string fileName) => "/storage/" + containerName + "/" + fileName;
    }

    // Three slides; the middle slide splits its token across two runs like PowerPoint does.
    private static byte[] BuildDeckWithText(string text) => BuildDeck([[text]]);

    private static byte[] BuildDeck(string[][]? slideRuns = null)
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new Presentation(
                new SlideIdList(),
                new SlideSize { Cx = 12192000, Cy = 6858000 });

            var ids = presentationPart.Presentation.SlideIdList!;
            slideRuns ??= [["Intro slide"], ["Talk by {{Speak", "erName}} on {{TalkTitle}}"], ["Duo {{Speaker1Name}} and {{Speaker2Name}}"], ["Tonight {{EventSubtitle}}!"]];
            uint id = 256;
            foreach (var runs in slideRuns)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                var paragraph = new D.Paragraph();
                foreach (var run in runs) paragraph.Append(new D.Run(new D.Text(run)));

                slidePart.Slide = new Slide(new CommonSlideData(new ShapeTree(
                    new NonVisualGroupShapeProperties(new NonVisualDrawingProperties { Id = 1, Name = "" }, new NonVisualGroupShapeDrawingProperties(), new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(),
                    new Shape(
                        new NonVisualShapeProperties(new NonVisualDrawingProperties { Id = 2, Name = "Box" }, new NonVisualShapeDrawingProperties(), new ApplicationNonVisualDrawingProperties()),
                        new ShapeProperties(new D.Transform2D(new D.Offset { X = 500000, Y = 500000 }, new D.Extents { Cx = 8000000, Cy = 1500000 })),
                        new TextBody(new D.BodyProperties(), paragraph)))));
                slidePart.Slide.Save();
                ids.Append(new SlideId { Id = id++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static List<string> SlideTexts(byte[] deck)
    {
        using var stream = new MemoryStream(deck);
        using var doc = PresentationDocument.Open(stream, false);
        var part = doc.PresentationPart!;
        return part.Presentation.SlideIdList!.Elements<SlideId>()
            .Select(s => string.Concat(((SlidePart)part.GetPartById(s.RelationshipId!)).Slide!.Descendants<D.Text>().Select(t => t.Text)))
            .ToList();
    }

    private static List<bool> SlideHidden(byte[] deck)
    {
        using var stream = new MemoryStream(deck);
        using var doc = PresentationDocument.Open(stream, false);
        var part = doc.PresentationPart!;
        return part.Presentation.SlideIdList!.Elements<SlideId>()
            .Select(s => ((SlidePart)part.GetPartById(s.RelationshipId!)).Slide!.Show?.Value == false)
            .ToList();
    }

    private static MeetupEvent CreateEvent(int speakers)
    {
        var group = new MeetupGroup
        {
            Id = Guid.NewGuid(),
            Name = "Group",
            PptxTemplateBlobUrl = "/storage/templates/" + FileName
        };
        var evt = new MeetupEvent
        {
            MeetupGroup = group,
            Title = "Event",
            EventDateTimeUtc = new DateTime(2026, 10, 20, 18, 30, 0, DateTimeKind.Utc)
        };
        for (var i = 0; i < speakers; i++)
        {
            evt.Sessions.Add(new EventSpeakerSession { OrderIndex = i, TalkTitle = $"Talk {i + 1}", Speaker = new Speaker { Name = $"Speaker {i + 1}" } });
        }

        return evt;
    }

    private static PptxGeneratorService Generator(byte[] deck) =>
        new(NullLogger<PptxGeneratorService>.Instance, new MemoryBlobStorage(deck));

    [Fact]
    public async Task LongVenueLine_ShrinksExplicitFontSize_ShortOneIsUntouched()
    {
        static byte[] Deck()
        {
            var deck = BuildDeckWithText("{{EventTime}} {{Venue}}");
            using var stream = new MemoryStream();
            stream.Write(deck);
            using (var doc = PresentationDocument.Open(stream, true))
            {
                var part = doc.PresentationPart!;
                var slide = ((SlidePart)part.GetPartById(part.Presentation.SlideIdList!.Elements<SlideId>().First().RelationshipId!)).Slide!;
                slide.Descendants<D.Run>().First().RunProperties = new D.RunProperties { FontSize = 4400 };
                slide.Save();
            }
            return stream.ToArray();
        }

        static int? Size(byte[] output)
        {
            using var stream = new MemoryStream(output);
            using var doc = PresentationDocument.Open(stream, false);
            var part = doc.PresentationPart!;
            var slide = ((SlidePart)part.GetPartById(part.Presentation.SlideIdList!.Elements<SlideId>().First().RelationshipId!)).Slide!;
            return slide.Descendants<D.RunProperties>().First().FontSize?.Value;
        }

        var shortEvent = CreateEvent(1);
        shortEvent.LocationOrPlatform = "CGI";
        var longEvent = CreateEvent(1);
        longEvent.LocationOrPlatform = "CGI King Street Nottingham & Online";

        Assert.Equal(4400, Size(await Generator(Deck()).GenerateEventDeckAsync(shortEvent)));
        Assert.True(Size(await Generator(Deck()).GenerateEventDeckAsync(longEvent)) < 4400);
    }

    [Fact]
    public async Task SingleSpeaker_FillsTokenSlidesAndHidesTwoSpeakerSlide()
    {
        var output = await Generator(BuildDeck()).GenerateEventDeckAsync(CreateEvent(1));
        var texts = SlideTexts(output);

        Assert.Equal(4, texts.Count);
        Assert.Equal("Intro slide", texts[0]);
        Assert.Equal("Talk by Speaker 1 on Talk 1", texts[1]);
        Assert.DoesNotContain("{{", texts[2]);
        Assert.StartsWith("Tonight ", texts[3]);
        Assert.DoesNotContain("{{", texts[3]);
        Assert.Equal([false, false, true, false], SlideHidden(output));
    }

    [Fact]
    public async Task TwoSpeakers_HidesSingleSpeakerSlide()
    {
        var output = await Generator(BuildDeck()).GenerateEventDeckAsync(CreateEvent(2));
        var texts = SlideTexts(output);

        Assert.Equal("Duo Speaker 1 and Speaker 2", texts[2]);
        Assert.Equal([false, true, false, false], SlideHidden(output));
    }

    [Fact]
    public async Task NextEventTokens_UseNextEventOrFallBackToTbc()
    {
        var deck = BuildDeckWithText("Next: {{NextEventSpeakers}} - {{NextEventTalkTitle}} on {{NextEventDate}}");
        var evt = CreateEvent(1);

        var without = SlideTexts(await Generator(deck).GenerateEventDeckAsync(evt));
        Assert.Equal("Next: TBC - TBC on TBC", without[0]);

        var next = CreateEvent(2);
        next.EventDateTimeUtc = new DateTime(2026, 11, 17, 18, 30, 0, DateTimeKind.Utc);
        var with = SlideTexts(await Generator(deck).GenerateEventDeckAsync(evt, next));
        Assert.Equal("Next: Speaker 1 & Speaker 2 - Talk 1 on Tuesday 17th November 2026", with[0]);
    }

    [Fact]
    public async Task DeckWithoutPlaceholders_FallsBackToBuiltInDeck()
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new Presentation(new SlideIdList());
            presentationPart.Presentation.Save();
        }

        var output = await Generator(stream.ToArray()).GenerateEventDeckAsync(CreateEvent(1));

        Assert.NotEmpty(SlideTexts(output));
    }

    [Fact]
    public async Task Preview_ListsSlidesWithCaptionTokensAndKind()
    {
        var preview = new PptxPreviewService(new MemoryBlobStorage(BuildDeck()), new MemoryCache(new MemoryCacheOptions()), NullLogger<PptxPreviewService>.Instance);

        var slides = await preview.GetSlidesAsync(FileName);

        Assert.Equal(4, slides.Count);
        Assert.Equal("Intro slide", slides[0].Caption);
        Assert.Empty(slides[0].Tokens);
        Assert.Equal(PptxTokens.SlideKind.Always, slides[0].Kind);
        Assert.Contains("{{TalkTitle}}", slides[1].Tokens);
        Assert.Contains("{{SpeakerName}}", slides[1].Tokens);
        Assert.Equal(PptxTokens.SlideKind.OneSpeaker, slides[1].Kind);
        Assert.Equal(PptxTokens.SlideKind.TwoSpeakers, slides[2].Kind);
        Assert.Equal(PptxTokens.SlideKind.Always, slides[3].Kind);
    }

    [Fact]
    public async Task Preview_RendersPngThumbnailAndFilledSlide()
    {
        var preview = new PptxPreviewService(new MemoryBlobStorage(BuildDeck()), new MemoryCache(new MemoryCacheOptions()), NullLogger<PptxPreviewService>.Instance);

        var thumb = await preview.RenderSlideAsync(FileName, 2, 480);
        var filled = await preview.RenderFilledSlideAsync(FileName, 2, 1, 960);

        Assert.NotNull(thumb);
        Assert.NotNull(filled);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, thumb![..4]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, filled![..4]);
        Assert.Null(await preview.RenderSlideAsync(FileName, 9, 480));
        Assert.Null(await preview.RenderSlideAsync("../evil.pptx", 1, 480));
    }

    [Fact]
    public void TemplateStore_ParsesFileNameFromStoredUrl()
    {
        Assert.True(PptxTemplateStore.TryParseFileName("/storage/templates/" + FileName, out var name));
        Assert.Equal(FileName, name);
        Assert.False(PptxTemplateStore.TryParseFileName("/storage/logos/a.png", out _));
        Assert.False(PptxTemplateStore.TryParseFileName(null, out _));
    }
}
