using System.Text;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class IcsCalendar
{
    public static string Build(MeetupEvent evt, string? description = null, DateTime? nowUtc = null)
    {
        var start = DateTime.SpecifyKind(evt.EventDateTimeUtc, DateTimeKind.Utc);
        var end = start.AddMinutes(evt.DurationMinutes > 0 ? evt.DurationMinutes : 90);
        var location = string.Join(", ", new[] { evt.VenueName, evt.LocationOrPlatform }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var details = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(description)) details.AppendLine(description);
        if (!string.IsNullOrWhiteSpace(evt.ZoomMeetingUrl)) details.AppendLine($"Zoom: {evt.ZoomMeetingUrl}");
        if (!string.IsNullOrWhiteSpace(evt.ZoomPasscode)) details.AppendLine($"Passcode: {evt.ZoomPasscode}");
        if (!string.IsNullOrWhiteSpace(evt.MeetupEventUrl)) details.AppendLine($"Meetup: {evt.MeetupEventUrl}");

        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Meetup Organiser//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{evt.Id}@meetuporganiser",
            $"DTSTAMP:{Format(nowUtc ?? DateTime.UtcNow)}",
            $"DTSTART:{Format(start)}",
            $"DTEND:{Format(end)}",
            $"SUMMARY:{Escape(evt.Title)}"
        };
        if (location.Length > 0) lines.Add($"LOCATION:{Escape(location)}");
        if (details.Length > 0) lines.Add($"DESCRIPTION:{Escape(details.ToString().TrimEnd())}");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        return string.Concat(lines.Select(l => Fold(l) + "\r\n"));
    }

    private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");

    // RFC 5545: lines are folded at 75 octets; continuation lines start with a space
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;

        var sb = new StringBuilder();
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 75)
            {
                sb.Append("\r\n ");
                bytes = 1;
            }
            sb.Append(rune.ToString());
            bytes += size;
        }
        return sb.ToString();
    }
}
