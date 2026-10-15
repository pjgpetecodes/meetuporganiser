using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class SpeakerConfirmationTests
{
    private static MeetupEvent Event() => new()
    {
        Title = "June, Meetup; Night",
        EventDateTimeUtc = new DateTime(2026, 6, 10, 17, 30, 0, DateTimeKind.Utc),
        DurationMinutes = 120,
        VenueName = "CGI",
        LocationOrPlatform = "Nottingham",
        ZoomMeetingUrl = "https://zoom.us/j/1"
    };

    [Fact]
    public void Ics_HasUtcTimesDurationAndEscapedText()
    {
        var ics = IcsCalendar.Build(Event(), nowUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains("DTSTART:20260610T173000Z\r\n", ics);
        Assert.Contains("DTEND:20260610T193000Z\r\n", ics);
        Assert.Contains("SUMMARY:June\\, Meetup\\; Night\r\n", ics);
        Assert.Contains("LOCATION:CGI\\, Nottingham", ics);
        Assert.Contains("https://zoom.us/j/1", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void Ics_FoldsLongLines()
    {
        var evt = Event();
        evt.Title = new string('x', 200);

        var ics = IcsCalendar.Build(evt);

        Assert.All(ics.Split("\r\n"), l => Assert.True(System.Text.Encoding.UTF8.GetByteCount(l) <= 75));
    }

    [Fact]
    public void Confirmation_IncludesEventDetailsAndEncodes()
    {
        var evt = Event();
        evt.Title = "<b>June</b>";

        var (subject, body) = SpeakerConfirmationEmail.Build("Ada", "My Talk", evt);

        Assert.Contains("June", subject);
        Assert.Contains("Hi Ada", body);
        Assert.Contains("My Talk", body);
        Assert.Contains("(UK time)", body);
        Assert.Contains("CGI, Nottingham", body);
        Assert.DoesNotContain("<b>June</b>", body);
    }
}
