using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class SocialPostTemplateTests
{
    private static MeetupEvent Event(EventFormat format = EventFormat.Hybrid)
    {
        var speaker = new Speaker { Name = "Adam Blakey", TwitterHandle = "amblakey", BlueSkyHandle = "adam.bsky.social" };
        var evt = new MeetupEvent
        {
            Title = "Blazor",
            Format = format,
            // 17:30 UTC on 26 Jan is 17:30 GMT
            EventDateTimeUtc = new DateTime(2026, 1, 26, 18, 30, 0, DateTimeKind.Utc),
            ShortBitlyUrl = "http://bit.ly/dotnetnottsjanuary2026",
            VenueName = "CGI Notts",
            MeetupGroup = new MeetupGroup { Name = ".NET Notts", SocialPostTemplate = SocialPostFormatter.DotNetNottsDefault }
        };
        evt.Sessions.Add(new EventSpeakerSession { Speaker = speaker, TalkTitle = "C# in the browser", OrderIndex = 0 });
        return evt;
    }

    [Fact]
    public void Render_MatchesDotNetNottsExample()
    {
        var text = SocialPostFormatter.Render(Event(), "Twitter", Event().MeetupGroup!.SocialPostTemplate);

        Assert.Equal(
            "Announcing this month's #DotNetNotts!\n\n" +
            "We have @amblakey coming along to talk to us about \"C# in the browser\".\n\n" +
            "⏰ Mon 26th Jan @ 18:30\n🌎 CGI Notts + Zoom\n🔗 http://bit.ly/dotnetnottsjanuary2026",
            text);
    }

    [Fact]
    public void Render_EndsWithThanksToSponsors()
    {
        var evt = Event();
        evt.MeetupGroup!.Sponsors.Add(new Sponsor { Name = "Raspberry Pi", TwitterHandle = "Raspberry_Pi", SortOrder = 0 });
        evt.MeetupGroup.Sponsors.Add(new Sponsor { Name = "PJG Creations Ltd", SortOrder = 1 });

        Assert.EndsWith("Thanks to @Raspberry_Pi and PJG Creations Ltd", SocialPostFormatter.Render(evt, "x", null));
        Assert.EndsWith("Thanks to Raspberry Pi and PJG Creations Ltd", SocialPostFormatter.Render(evt, "linkedin", SocialPostFormatter.DotNetNottsDefault));
    }

    [Theory]
    [InlineData("bluesky", "@adam.bsky.social")]
    [InlineData("linkedin", "Adam Blakey")]
    public void Render_UsesPlatformMention(string platform, string expected) =>
        Assert.Contains(expected, SocialPostFormatter.Render(Event(), platform, null));

    [Theory]
    [InlineData(EventFormat.InPerson, "CGI Notts")]
    [InlineData(EventFormat.Online, "Zoom")]
    public void Where_FollowsFormat(EventFormat format, string expected) =>
        Assert.Contains("🌎 " + expected + "\n", SocialPostFormatter.Render(Event(format), "x", SocialPostFormatter.DotNetNottsDefault) + "\n");
}
