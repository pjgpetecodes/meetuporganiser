using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class SpeakerInviteEmailTests
{
    [Fact]
    public void Build_IncludesNameEventAndLink()
    {
        var (subject, body) = SpeakerInviteEmail.Build("Ada", "June Meetup", new DateTime(2026, 6, 10, 17, 30, 0, DateTimeKind.Utc), "https://x/speaker/submit/abc");

        Assert.Contains("June Meetup", subject);
        Assert.Contains("Hi Ada", body);
        Assert.Contains("https://x/speaker/submit/abc", body);
        Assert.Contains("(UK time)", body);
    }

    [Fact]
    public void Build_PlaceholderOrMissingNameUsesGenericGreeting()
    {
        var (_, body) = SpeakerInviteEmail.Build(Speaker.PlaceholderName, "Ev", null, "https://x");

        Assert.Contains("Hi there", body);
        Assert.DoesNotContain("UK time", body);
    }

    [Fact]
    public void Build_HtmlEncodesContent()
    {
        var (_, body) = SpeakerInviteEmail.Build("<i>Eve</i>", "<b>Ev</b>", null, "https://x?a=1&b=2");

        Assert.DoesNotContain("<i>Eve</i>", body);
        Assert.DoesNotContain("<b>Ev</b>", body);
        Assert.Contains("a=1&amp;b=2", body);
    }
}
