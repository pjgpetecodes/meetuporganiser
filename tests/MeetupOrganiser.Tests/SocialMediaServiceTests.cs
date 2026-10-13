using System.Net.Http;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class SocialMediaServiceTests
{
    private MeetupEvent CreateSampleEvent()
    {
        var group = new MeetupGroup
        {
            Id = Guid.NewGuid(),
            Name = ".NET Notts",
            DefaultHashtags = "@dotnetnotts #dotnet"
        };

        var speaker = new Speaker
        {
            Id = Guid.NewGuid(),
            Name = "Pete Gallagher",
            TwitterHandle = "pete_codes",
            LinkedInUrl = "https://linkedin.com/in/pjgpetecodes"
        };

        var evt = new MeetupEvent
        {
            Id = Guid.NewGuid(),
            MeetupGroup = group,
            MeetupGroupId = group.Id,
            Title = "Cloud-Native .NET 10",
            Description = "Join us for an exciting evening learning about .NET 10.",
            EventDateTimeUtc = new DateTime(2026, 11, 15, 18, 30, 0, DateTimeKind.Utc),
            DurationMinutes = 90,
            LocationOrPlatform = "Zoom Online",
            ShortBitlyUrl = "https://bit.ly/dotnetnotts-nov26",
            Status = EventStatus.Scheduled
        };

        evt.Sessions.Add(new EventSpeakerSession
        {
            Id = Guid.NewGuid(),
            MeetupEventId = evt.Id,
            Speaker = speaker,
            SpeakerId = speaker.Id,
            TalkTitle = "Zero to Hero with Azure & .NET",
            TalkDescription = "A deep dive into cloud architecture.",
            OrderIndex = 0
        });

        return evt;
    }

    [Fact]
    public void GeneratePlatformPostText_Twitter_IncludesHandleAndUrl()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var httpClient = new HttpClient();
        var service = new SocialMediaService(httpClient, config, NullLogger<SocialMediaService>.Instance);
        var evt = CreateSampleEvent();

        // Act
        var text = service.GeneratePlatformPostText(evt, "Twitter");

        // Assert
        Assert.Contains("@pete_codes", text);
        Assert.Contains("Zero to Hero", text);
        Assert.Contains("https://bit.ly/dotnetnotts-nov26", text);
    }

    [Fact]
    public void GetPlatformWebIntentUrl_Twitter_GeneratesValidIntentLink()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var httpClient = new HttpClient();
        var service = new SocialMediaService(httpClient, config, NullLogger<SocialMediaService>.Instance);
        var evt = CreateSampleEvent();

        // Act
        var intentUrl = service.GetPlatformWebIntentUrl(evt, "Twitter");

        // Assert
        Assert.StartsWith("https://twitter.com/intent/tweet?text=", intentUrl);
        Assert.Contains("pete_codes", intentUrl);
    }

    [Fact]
    public void GetPlatformWebIntentUrl_LinkedIn_GeneratesValidIntentLink()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var httpClient = new HttpClient();
        var service = new SocialMediaService(httpClient, config, NullLogger<SocialMediaService>.Instance);
        var evt = CreateSampleEvent();

        // Act
        var intentUrl = service.GetPlatformWebIntentUrl(evt, "LinkedIn");

        // Assert
        Assert.StartsWith("https://www.linkedin.com/feed/?shareActive=true&text=", intentUrl);
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
