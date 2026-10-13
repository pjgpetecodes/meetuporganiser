using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using Xunit;

namespace MeetupOrganiser.Tests;

public class EventHeaderTests
{
    private static MeetupEvent Event(EventFormat format, string? venue = null, string? subtitle = null) => new()
    {
        EventDateTimeUtc = new DateTime(2026, 4, 27, 17, 30, 0, DateTimeKind.Utc),
        Format = format,
        VenueName = venue,
        Subtitle = subtitle
    };

    [Fact]
    public void DateLine_UsesUkDateWithOrdinal() =>
        Assert.Equal("MONDAY 27TH APRIL 2026", EventHeader.DateLine(Event(EventFormat.Hybrid)));

    [Theory]
    [InlineData(EventFormat.InPerson, "AT CGI ONLY FROM 18:30 BST!")]
    [InlineData(EventFormat.Hybrid, "AT CGI AND ON ZOOM FROM 18:30 BST!")]
    [InlineData(EventFormat.Online, "ONLINE ON ZOOM FROM 18:30 BST!")]
    public void VenueLine_DependsOnFormat(EventFormat format, string expected) =>
        Assert.Equal(expected, EventHeader.VenueLine(Event(format, "CGI")));

    [Fact]
    public void VenueLine_IgnoresVenueWhenOnline() =>
        Assert.DoesNotContain("OFFICE", EventHeader.VenueLine(Event(EventFormat.Online, "Office")));

    [Theory]
    [InlineData(1, "ST")]
    [InlineData(2, "ND")]
    [InlineData(3, "RD")]
    [InlineData(11, "TH")]
    [InlineData(22, "ND")]
    public void DateLine_OrdinalSuffixes(int day, string suffix)
    {
        var e = Event(EventFormat.Hybrid);
        e.EventDateTimeUtc = new DateTime(2026, 6, day, 12, 0, 0, DateTimeKind.Utc);
        Assert.Contains($" {day}{suffix} ", EventHeader.DateLine(e));
    }

    [Fact]
    public void Subtitle_IsNullWhenBlank()
    {
        Assert.Null(EventHeader.Subtitle(Event(EventFormat.Hybrid)));
        Assert.Null(EventHeader.Subtitle(Event(EventFormat.Hybrid, subtitle: "  ")));
        Assert.Equal("Workshop!", EventHeader.Subtitle(Event(EventFormat.Hybrid, subtitle: " Workshop! ")));
    }
}
