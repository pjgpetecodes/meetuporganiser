using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class SocialImageGeneratorServiceTests
{
    private MeetupEvent CreateEventWithSpeakers(int speakerCount)
    {
        var group = new MeetupGroup
        {
            Id = Guid.NewGuid(),
            Name = ".NET Notts",
            PrimaryColorHex = "#005596",
            SecondaryColorHex = "#fdb813"
        };

        var evt = new MeetupEvent
        {
            Id = Guid.NewGuid(),
            MeetupGroup = group,
            MeetupGroupId = group.Id,
            Title = "Modern Cloud Applications with .NET 10",
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
                Name = (i == 0) ? "Luce Carter" : $"Speaker {i + 1}",
                TwitterHandle = $"speaker{i + 1}",
                PhotoBlobUrl = (i == 0) ? "/images/speakers/Luce Carter.jpg" : null
            };

            evt.Sessions.Add(new EventSpeakerSession
            {
                Id = Guid.NewGuid(),
                MeetupEventId = evt.Id,
                Speaker = speaker,
                SpeakerId = speaker.Id,
                TalkTitle = $"Talk Title for Speaker {i + 1}",
                TalkDescription = "Engaging session about cloud native tools.",
                OrderIndex = i
            });
        }

        return evt;
    }

    [Fact]
    public async Task GenerateSocialCardAsync_SingleSpeaker_ReturnsValidPngBytes()
    {
        // Arrange
        var service = new SocialImageGeneratorService(NullLogger<SocialImageGeneratorService>.Instance);
        var evt = CreateEventWithSpeakers(1);

        // Act
        var pngBytes = await service.GenerateSocialCardAsync(evt);

        // Assert
        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 0);
        // Verify PNG magic header: 137 80 78 71 (0x89, 'P', 'N', 'G')
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);
    }

    [Fact]
    public async Task GenerateSocialCardAsync_TwoSpeakers_ReturnsValidPngBytes()
    {
        // Arrange
        var service = new SocialImageGeneratorService(NullLogger<SocialImageGeneratorService>.Instance);
        var evt = CreateEventWithSpeakers(2);

        // Act
        var pngBytes = await service.GenerateSocialCardAsync(evt);

        // Assert
        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 0);
        // Verify PNG magic header
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);
    }
}
