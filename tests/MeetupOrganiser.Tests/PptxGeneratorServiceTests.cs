using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class PptxGeneratorServiceTests
{
    private class DummyHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "MeetupOrganiser.Tests";
        public string ContentRootPath { get; set; } = FindRepoRoot();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "MeetupOrganiser.slnx")))
            {
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return dir ?? AppContext.BaseDirectory;
        }
    }

    private class DummyBlobStorageService : Core.Interfaces.IBlobStorageService
    {
        public Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://blob.test/" + fileName);
        public Task<Stream> DownloadAsync(string containerName, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> DeleteAsync(string containerName, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public string GetBlobUrl(string containerName, string fileName) =>
            "https://blob.test/" + fileName;
    }

    private MeetupEvent CreateEventWithSpeakers(int speakerCount)
    {
        var group = new MeetupGroup
        {
            Id = Guid.NewGuid(),
            Name = ".NET Notts"
        };

        var evt = new MeetupEvent
        {
            Id = Guid.NewGuid(),
            MeetupGroup = group,
            MeetupGroupId = group.Id,
            Title = "Building Cloud-Native Solutions with .NET 10",
            EventDateTimeUtc = new DateTime(2026, 10, 20, 18, 30, 0, DateTimeKind.Utc),
            DurationMinutes = 90,
            LocationOrPlatform = "Zoom Online",
            ShortBitlyUrl = "https://bit.ly/dotnetnotts-oct26"
        };

        for (int i = 0; i < speakerCount; i++)
        {
            var speaker = new Speaker
            {
                Id = Guid.NewGuid(),
                Name = $"Speaker {i + 1}",
                TwitterHandle = $"speaker{i + 1}",
                Bio = $"Bio for speaker {i + 1}"
            };

            evt.Sessions.Add(new EventSpeakerSession
            {
                Id = Guid.NewGuid(),
                MeetupEventId = evt.Id,
                Speaker = speaker,
                SpeakerId = speaker.Id,
                TalkTitle = $"Exciting Talk {i + 1}",
                TalkDescription = "Detailed description of the presentation.",
                OrderIndex = i
            });
        }

        return evt;
    }

    [Fact]
    public async Task GenerateEventDeckAsync_SingleSpeaker_ReturnsValidPptxBytes()
    {
        // Arrange
        var service = new PptxGeneratorService(NullLogger<PptxGeneratorService>.Instance);
        var evt = CreateEventWithSpeakers(1);

        // Act
        var pptxBytes = await service.GenerateEventDeckAsync(evt);

        // Assert
        Assert.NotNull(pptxBytes);
        Assert.True(pptxBytes.Length > 0);
        // Verify ZIP / OpenXML header (PK\x03\x04 = 0x50, 0x4B, 0x03, 0x04)
        Assert.Equal(0x50, pptxBytes[0]);
        Assert.Equal(0x4B, pptxBytes[1]);
        Assert.Equal(0x03, pptxBytes[2]);
        Assert.Equal(0x04, pptxBytes[3]);
    }

    [Fact]
    public async Task GenerateEventDeckAsync_TwoSpeakers_ReturnsValidPptxBytes()
    {
        // Arrange
        var service = new PptxGeneratorService(NullLogger<PptxGeneratorService>.Instance);
        var evt = CreateEventWithSpeakers(2);

        // Act
        var pptxBytes = await service.GenerateEventDeckAsync(evt);

        // Assert
        Assert.NotNull(pptxBytes);
        Assert.True(pptxBytes.Length > 0);
        // Verify ZIP / OpenXML header
        Assert.Equal(0x50, pptxBytes[0]);
        Assert.Equal(0x4B, pptxBytes[1]);
        Assert.Equal(0x03, pptxBytes[2]);
        Assert.Equal(0x04, pptxBytes[3]);
    }
}
