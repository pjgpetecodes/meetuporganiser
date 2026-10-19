using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class SpeakerInviteEmailTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CfpEmail_ExplainsDirectFormAccessExpiryAndBookingWithoutUnsafeMarkup(bool preferredDate)
    {
        var (subject, body) = SpeakerInviteEmail.BuildCfpVerification("<b>Group</b>\r\nName", "https://example.com/speaker/choose/abc?dateId=123&test=1", preferredDate);
        Assert.DoesNotContain("\r", subject);
        Assert.DoesNotContain("\n", subject);
        Assert.Contains("complete your speaker profile and submit your talk", subject);
        Assert.Contains("&lt;b&gt;Group&lt;/b&gt;", body);
        Assert.DoesNotContain("<b>Group</b>", body);
        Assert.Contains("dateId=123&amp;test=1", body);
        Assert.Contains("takes you straight to the submission form", body);
        Assert.Contains("30 minutes after it was requested", body);
        Assert.Contains("Your place is not reserved by opening this link.", body);
        Assert.Contains("If you didn't request this email", body);
        Assert.Equal(preferredDate, body.Contains("Your preferred date is included", StringComparison.Ordinal));
    }

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
