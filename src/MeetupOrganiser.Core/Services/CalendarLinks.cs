using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class CalendarLinks
{
    public static string Google(MeetupEvent evt, string? description = null)
    {
        var (start, end) = Window(evt);
        return "https://calendar.google.com/calendar/render?action=TEMPLATE"
            + $"&text={Uri.EscapeDataString(evt.Title)}"
            + $"&dates={start:yyyyMMdd'T'HHmmss'Z'}/{end:yyyyMMdd'T'HHmmss'Z'}"
            + $"&details={Uri.EscapeDataString(description ?? string.Empty)}"
            + $"&location={Uri.EscapeDataString(Location(evt))}";
    }

    public static string Outlook(MeetupEvent evt, string? description = null)
    {
        var (start, end) = Window(evt);
        return "https://outlook.live.com/calendar/0/deeplink/compose?path=%2Fcalendar%2Faction%2Fcompose&rru=addevent"
            + $"&subject={Uri.EscapeDataString(evt.Title)}"
            + $"&startdt={Uri.EscapeDataString(start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}"
            + $"&enddt={Uri.EscapeDataString(end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}"
            + $"&body={Uri.EscapeDataString(description ?? string.Empty)}"
            + $"&location={Uri.EscapeDataString(Location(evt))}";
    }

    private static (DateTime Start, DateTime End) Window(MeetupEvent evt)
    {
        var start = DateTime.SpecifyKind(evt.EventDateTimeUtc, DateTimeKind.Utc);
        return (start, start.AddMinutes(evt.DurationMinutes > 0 ? evt.DurationMinutes : 90));
    }

    private static string Location(MeetupEvent evt) =>
        string.Join(", ", new[] { evt.VenueName, evt.LocationOrPlatform }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
