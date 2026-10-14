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
    private static byte[] BuildDeck()
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new Presentation(
                new SlideIdList(),
                new SlideSize { Cx = 12192000, Cy = 6858000 });

            var ids = presentationPart.Presentation.SlideIdList!;
            string[][] slideRuns = [["Intro {{SpeakerName}}"], ["Talk by {{Speak", "erName}} on {{TalkTitle}}"], ["Thanks {{SpeakerName}}"]];
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

    private static MeetupEvent CreateEvent(int speakers, int? one, int? two)
    {
        var group = new MeetupGroup
        {
            Id = Guid.NewGuid(),
            Name = "Group",
            PptxTemplateBlobUrl = "/storage/templates/" + FileName,
            TemplateSlideOneSpeaker = one,
            TemplateSlideTwoSpeakers = two
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
    public async Task ChosenSlide_IsTheOnlySlideReplaced()
    {
        var deck = BuildDeck();

        var output = await Generator(deck).GenerateEventDeckAsync(CreateEvent(1, one: 2, two: 3));
        var texts = SlideTexts(output);

        Assert.Equal(3, texts.Count);
        Assert.Equal("Intro {{SpeakerName}}", texts[0]);
        Assert.Equal("Talk by Speaker 1 on Talk 1", texts[1]);
        Assert.Equal("Thanks {{SpeakerName}}", texts[2]);
    }

    [Fact]
    public async Task TwoSpeakers_UsesTheTwoSpeakerSlide()
    {
        var output = await Generator(BuildDeck()).GenerateEventDeckAsync(CreateEvent(2, one: 2, two: 3));
        var texts = SlideTexts(output);

        Assert.Equal("Thanks Speaker 1", texts[2]);
        Assert.Equal("Talk by {{Speak" + "erName}} on {{TalkTitle}}", texts[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task MissingOrOutOfRangeSelection_FallsBackToBuiltInDeck(int? slide)
    {
        var output = await Generator(BuildDeck()).GenerateEventDeckAsync(CreateEvent(1, one: slide, two: slide));

        // The built-in deck is 13 slides; the 3-slide fixture must not be returned.
        Assert.NotEqual(3, SlideTexts(output).Count);
    }

    [Fact]
    public async Task Preview_ListsSlidesWithCaptionAndTokens()
    {
        var preview = new PptxPreviewService(new MemoryBlobStorage(BuildDeck()), new MemoryCache(new MemoryCacheOptions()), NullLogger<PptxPreviewService>.Instance);

        var slides = await preview.GetSlidesAsync(FileName);

        Assert.Equal(3, slides.Count);
        Assert.Equal("Intro {{SpeakerName}}", slides[0].Caption);
        Assert.Contains("{{TalkTitle}}", slides[1].Tokens);
        Assert.Contains("{{SpeakerName}}", slides[1].Tokens);
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
