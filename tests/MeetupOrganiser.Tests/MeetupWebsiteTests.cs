using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class MeetupWebsiteTests
{
    private static readonly string Png = Convert.ToBase64String([137, 80, 78, 71, 13, 10, 26, 10, 0]);
    private static MeetupEvent Event() => new()
    {
        MeetupGroup = new() { MeetupUrlName = "test-group" },
        EventDateTimeUtc = new(2027, 1, 25, 18, 30, 0, DateTimeKind.Utc),
        DurationMinutes = 90, VenueName = "Hall", LocationOrPlatform = "https://zoom.us/example"
    };

    [Theory]
    [InlineData(1, 25, 18, 30, "2027-01-25", "18:30", "20:00", "GMT")]
    [InlineData(6, 1, 17, 30, "2027-06-01", "18:30", "20:00", "BST")]
    [InlineData(6, 1, 23, 30, "2027-06-02", "00:30", "02:00", "BST")]
    public void Prepare_UsesEditedContentAndUkSchedule(int month, int day, int hour, int minute,
        string date, string start, string end, string abbreviation)
    {
        var meetup = Event();
        meetup.EventDateTimeUtc = new(2027, month, day, hour, minute, 0, DateTimeKind.Utc);
        var result = MeetupWebsite.Prepare(meetup, "Edited title", "Edited **Markdown**", Png);
        Assert.Equal("Edited title", result.Title);
        Assert.Equal("Edited **Markdown**", result.Description);
        Assert.Equal(date, result.StartDate);
        Assert.Equal(start, result.StartTime);
        Assert.Equal(end, result.EndTime);
        Assert.Equal(abbreviation, result.TimeZoneLabel);
        Assert.Equal("Europe/London", result.TimeZone);
        Assert.Equal("create", result.Operation);
        Assert.Null(result.EventUrl);
        Assert.InRange(result.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public void Prepare_TransfersOnlineUrlSeparatelyFromPhysicalVenue()
    {
        var meetup = Event();
        meetup.LocationOrPlatform = "Town centre";
        meetup.ZoomMeetingUrl = "https://zoom.us/saved";
        foreach (var format in new[] { EventFormat.Hybrid, EventFormat.Online, EventFormat.InPerson })
        {
            meetup.Format = format;
            var handoff = MeetupWebsite.Prepare(meetup, "Title", "Description", Png);
            Assert.Equal(format == EventFormat.InPerson ? null : meetup.ZoomMeetingUrl, handoff.OnlineEventUrl);
            Assert.Equal("Town centre", handoff.Location);
            Assert.Equal("Hall", handoff.VenueName);
        }
    }

    [Fact]
    public void Prepare_IncludesOnlyFirstPublicSpeakerInSessionOrderAndHonoursBioPreference()
    {
        var meetup = Event();
        var first = new Speaker { Name = "Alex", Bio = "Original bio", BioThirdPerson = "Published bio",
            Email = "private@example.test", TwitterHandle = "@alex", WebsiteUrl = "https://alex.example" };
        var second = new Speaker { Name = "Sam" };
        meetup.Sessions = [
            new() { OrderIndex = 3, Speaker = second },
            new() { OrderIndex = 1, Speaker = first },
            new() { OrderIndex = 2, Speaker = first },
            new() { OrderIndex = 0, Speaker = new() { Name = Speaker.PlaceholderName } }
        ];
        var handoff = MeetupWebsite.Prepare(meetup, "Title", "Description", Png);
        Assert.Equal(2, handoff.SpeakerCount);
        Assert.Equal("Alex", handoff.Speaker!.Name);
        Assert.Equal("Published bio", handoff.Speaker.Bio);
        Assert.Equal("@alex", handoff.Speaker.Twitter);
        Assert.Equal("Original bio", MeetupWebsite.Prepare(meetup, "Title", "Description", Png, "original").Speaker!.Bio);
        Assert.DoesNotContain("private@example.test", System.Text.Json.JsonSerializer.Serialize(handoff));
    }

    [Fact]
    public void Prepare_OtherSocialUsesWebsiteThenBlueskyThenGitHub()
    {
        var meetup = Event();
        var speaker = new Speaker { Name = "Alex", WebsiteUrl = "https://alex.example",
            BlueSkyHandle = "@alex.bsky.social", GitHubHandle = "@alex" };
        meetup.Sessions = [new() { Speaker = speaker }];
        string? Other() => MeetupWebsite.Prepare(meetup, "Title", "Description", Png).Speaker!.Website;
        Assert.Equal("https://alex.example", Other());
        speaker.WebsiteUrl = null;
        Assert.Equal("https://bsky.app/profile/alex.bsky.social", Other());
        speaker.BlueSkyHandle = "https://bsky.app/profile/alex.example";
        Assert.Equal(speaker.BlueSkyHandle, Other());
        speaker.BlueSkyHandle = null;
        Assert.Equal("https://github.com/alex", Other());
        speaker.GitHubHandle = "javascript:alert(1)";
        Assert.Throws<ArgumentException>(() => Other());
    }

    [Fact]
    public void Prepare_NormalisesStoredTrackingUrlButPreservesExactEventIdentity()
    {
        var meetup = Event();
        meetup.MeetupEventUrl = " https://www.meetup.com/test-group/events/123/?utm_source=email#details ";
        Assert.Equal("https://www.meetup.com/test-group/events/123/",
            MeetupWebsite.Prepare(meetup, "Title", "Description", Png).EventUrl);
        foreach (var invalid in new[] {
            "https://evil.example/test-group/events/123/?tracking=1",
            "https://www.meetup.com/other-group/events/123/?tracking=1",
            "https://user@www.meetup.com/test-group/events/123/",
            "https://www.meetup.com/test-group/events/",
            "not a url" })
        {
            meetup.MeetupEventUrl = invalid;
            var error = Assert.Throws<ArgumentException>(() => MeetupWebsite.Prepare(meetup, "Title", "Description", Png));
            Assert.Contains("saved Meetup URL", error.Message);
            Assert.Contains("Edit Meetup", error.Message);
        }
    }

    [Fact]
    public void Prepare_EndDateRollsOverAndExistingEventIsAnUpdate()
    {
        var meetup = Event();
        meetup.EventDateTimeUtc = new(2027, 1, 25, 23, 30, 0, DateTimeKind.Utc);
        meetup.MeetupEventUrl = "https://meetup.com/test-group/events/123";
        var result = MeetupWebsite.Prepare(meetup, "Title", "Description", Png);
        Assert.Equal("2027-01-26", result.EndDate);
        Assert.Equal("01:00", result.EndTime);
        Assert.Equal("update", result.Operation);
        Assert.Equal("https://www.meetup.com/test-group/events/123/", result.EventUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("SGVsbG8=")]
    public void Prepare_RejectsMissingOrInvalidPng(string image) =>
        Assert.Throws<ArgumentException>(() => MeetupWebsite.Prepare(Event(), "Title", "Description", image));

    [Fact]
    public void Prepare_RejectsMissingGroupAndOversizeContent()
    {
        var meetup = Event();
        Assert.Throws<ArgumentException>(() => MeetupWebsite.Prepare(meetup, new string('x', 251), "Description", Png));
        Assert.Throws<ArgumentException>(() => MeetupWebsite.Prepare(meetup, "Title", new string('x', 60001), Png));
        Assert.Throws<ArgumentException>(() => MeetupWebsite.Prepare(meetup, "Title", "Description", new string('A', 6990512)));
        meetup.MeetupGroup!.MeetupUrlName = null;
        Assert.Throws<ArgumentException>(() => MeetupWebsite.Prepare(meetup, "Title", "Description", Png));
    }

    [Theory]
    [InlineData("http://www.meetup.com/test-group/events/123/")]
    [InlineData("https://www.meetup.com.evil.test/test-group/events/123/")]
    [InlineData("https://user@www.meetup.com/test-group/events/123/")]
    [InlineData("https://www.meetup.com:444/test-group/events/123/")]
    [InlineData("https://www.meetup.com/other-group/events/123/")]
    [InlineData("https://www.meetup.com/test-group/events/create/")]
    [InlineData("https://www.meetup.com/test-group/events/123/edit/")]
    [InlineData("https://www.meetup.com/test-group/events/123/?foo=bar")]
    [InlineData("https://www.meetup.com/test-group/events/123/#fragment")]
    public void Completion_RejectsUnsafeOrWrongEventUrls(string url) =>
        Assert.Throws<ArgumentException>(() => MeetupWebsite.ValidateEventUrl(url, "test-group"));

    [Fact]
    public void Completion_RejectsAccidentalReplacementOfAnExistingEvent()
    {
        const string linked = "https://www.meetup.com/test-group/events/123/";
        Assert.Throws<ArgumentException>(() => MeetupWebsite.ValidateEventUrl(
            "https://www.meetup.com/test-group/events/456/", "test-group", linked));
        Assert.Equal(("https://www.meetup.com/test-group/events/123/", "123"),
            MeetupWebsite.ValidateEventUrl("https://meetup.com/test-group/events/123", "test-group", linked));
    }
}
