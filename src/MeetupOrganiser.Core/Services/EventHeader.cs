using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class EventHeader
{
    public const string DefaultVenue = "CGI";

    public static string FormatLabel(EventFormat format) => format switch
    {
        EventFormat.InPerson => "In Person",
        EventFormat.Online => "Online",
        _ => "Hybrid"
    };

    /// <summary>Event start in UK local time.</summary>
    public static DateTime ToUkTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));

    /// <summary>The first header line, e.g. "MONDAY 27TH APRIL 2026".</summary>
    public static string DateLine(MeetupEvent meetupEvent)
    {
        var uk = ToUkTime(meetupEvent.EventDateTimeUtc);
        return $"{uk:dddd} {uk.Day}{OrdinalSuffix(uk.Day)} {uk:MMMM yyyy}".ToUpperInvariant();
    }

    /// <summary>The second header line, which depends on the event format, e.g. "AT CGI AND ON ZOOM FROM 18:30 BST".</summary>
    public static string VenueLine(MeetupEvent meetupEvent)
    {
        var uk = ToUkTime(meetupEvent.EventDateTimeUtc);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London").IsDaylightSavingTime(uk) ? "BST" : "GMT";
        var time = $"{uk:HH:mm} {zone}";
        var venue = string.IsNullOrWhiteSpace(meetupEvent.VenueName) ? DefaultVenue : meetupEvent.VenueName.Trim();

        var line = meetupEvent.Format switch
        {
            EventFormat.InPerson => $"AT {venue} ONLY FROM {time}!",
            EventFormat.Online => $"ONLINE ON ZOOM FROM {time}!",
            _ => $"AT {venue} AND ON ZOOM FROM {time}!"
        };
        return line.ToUpperInvariant();
    }

    /// <summary>Optional third line; blank when the event has no subtitle.</summary>
    public static string? Subtitle(MeetupEvent meetupEvent) =>
        string.IsNullOrWhiteSpace(meetupEvent.Subtitle) ? null : meetupEvent.Subtitle.Trim();

    private static string OrdinalSuffix(int day) => day switch
    {
        11 or 12 or 13 => "th",
        _ => (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
    };
}
