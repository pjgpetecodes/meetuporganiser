using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class MeetupServiceDescriptionTests
{
    private static MeetupService CreateService() =>
        new(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<MeetupService>.Instance);

    [Fact]
    public void GenerateMeetupDescription_FormatsSingleSpeakerAndHybridDetails()
    {
        var speaker = new Speaker
        {
            Id = Guid.NewGuid(),
            Name = "Adam Blakey",
            Bio = "Adam is a software engineer and mathematician.",
            BlueSkyHandle = "adam.blakey.family",
            TwitterHandle = "@amblakey",
            LinkedInUrl = "https://www.linkedin.com/in/adamblakey",
            WebsiteUrl = "https://adam.blakey.family/"
        };
        var meetupEvent = new MeetupEvent
        {
            Title = "C# in the browser",
            Description = "Join us to explore C# in modern web browsers.",
            EventDateTimeUtc = new DateTime(2026, 5, 20, 18, 30, 0, DateTimeKind.Utc),
            LocationOrPlatform = "CGI on King Street, Nottingham",
            ZoomMeetingUrl = "https://zoom.example/meeting",
            ZoomPasscode = "1234",
            Sessions =
            [
                new EventSpeakerSession
                {
                    TalkTitle = "C# in the browser: An intro to Blazor and WebAssembly",
                    TalkDescription = "Explore Blazor and WebAssembly.",
                    Speaker = speaker
                }
            ]
        };

        var description = CreateService().GenerateMeetupDescription(meetupEvent).Replace("\r\n", "\n");

        Assert.Contains("Wednesday, May 20, 2026 at 7:30 PM UK time", description);
        Assert.Contains("C# in the browser: An intro to Blazor and WebAssembly - Adam Blakey", description);
        Assert.Contains("Explore Blazor and WebAssembly.", description);
        Assert.Contains("Our Speaker:", description);
        Assert.Contains("Adam is a software engineer and mathematician.", description);
        Assert.Contains("Bluesky: https://bsky.app/profile/adam.blakey.family", description);
        Assert.Contains("X: https://x.com/amblakey", description);
        Assert.Contains("LinkedIn: https://www.linkedin.com/in/adamblakey", description);
        Assert.Contains("Personal Site: https://adam.blakey.family/", description);
        Assert.Contains("How to find us:\nCGI on King Street, Nottingham", description);
        Assert.Contains("Online on Zoom: https://zoom.example/meeting", description);
        Assert.Contains("Passcode: 1234", description);
        Assert.DoesNotContain("Our Speakers:", description);
    }

    [Fact]
    public void GenerateMeetupDescription_FormatsTwoSpeakersInSessionOrder()
    {
        var firstSpeaker = new Speaker { Id = Guid.NewGuid(), Name = "First Speaker", Bio = "First bio." };
        var secondSpeaker = new Speaker { Id = Guid.NewGuid(), Name = "Second Speaker", Bio = "Second bio." };
        var meetupEvent = new MeetupEvent
        {
            Title = "Two talk meetup",
            EventDateTimeUtc = new DateTime(2026, 5, 20, 18, 30, 0, DateTimeKind.Utc),
            Sessions =
            [
                new EventSpeakerSession
                {
                    OrderIndex = 2,
                    TalkTitle = "Second talk",
                    TalkDescription = "Second talk description.",
                    Speaker = secondSpeaker
                },
                new EventSpeakerSession
                {
                    OrderIndex = 1,
                    TalkTitle = "First talk",
                    TalkDescription = "First talk description.",
                    Speaker = firstSpeaker
                }
            ]
        };

        var description = CreateService().GenerateMeetupDescription(meetupEvent);

        Assert.Contains("Our Speakers:", description);
        Assert.DoesNotContain("Our Speaker:\n", description);
        Assert.True(description.IndexOf("First talk - First Speaker", StringComparison.Ordinal)
            < description.IndexOf("Second talk - Second Speaker", StringComparison.Ordinal));
        Assert.Contains("First bio.", description);
        Assert.Contains("Second bio.", description);
    }
}
