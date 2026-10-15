using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class SpeakerSubmittedEmailTests
{
    [Fact]
    public void Build_IncludesSpeakerTalkEventAndLink()
    {
        var (subject, body) = SpeakerSubmittedEmail.Build("Ada Lovelace", "Engines", "June Meetup", "https://x/events/edit/1");

        Assert.Contains("Ada Lovelace", subject);
        Assert.Contains("June Meetup", subject);
        Assert.Contains("Engines", body);
        Assert.Contains("https://x/events/edit/1", body);
    }

    [Fact]
    public void Build_HtmlEncodesUserContent()
    {
        var (_, body) = SpeakerSubmittedEmail.Build("<b>Eve</b>", "<script>x</script>", "Ev", "https://x");

        Assert.DoesNotContain("<script>", body);
        Assert.DoesNotContain("<b>Eve</b>", body);
    }

    [Fact]
    public void Build_HandlesMissingTalkTitle()
    {
        var (_, body) = SpeakerSubmittedEmail.Build("Ada", null, "Ev", "https://x");

        Assert.Contains("no talk title", body);
    }
}
