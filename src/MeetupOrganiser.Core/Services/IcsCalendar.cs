using System.Text;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class IcsCalendar
{
    /// <summary>
    /// Builds an iCalendar file. When both organiser and attendee emails are supplied it is a
    /// METHOD:REQUEST meeting invite (RSVP replies go to the organiser); otherwise a plain event.
    /// </summary>
    public static string Build(
        MeetupEvent evt,
        string? description = null,
        DateTime? nowUtc = null,
        string? organiserEmail = null,
        string? attendeeEmail = null,
        string? attendeeName = null,
        string? organiserName = null)
    {
        var isInvite = !string.IsNullOrWhiteSpace(organiserEmail) && !string.IsNullOrWhiteSpace(attendeeEmail);
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
            isInvite ? "METHOD:REQUEST" : "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{evt.Id}@meetuporganiser",
            $"DTSTAMP:{Format(nowUtc ?? DateTime.UtcNow)}",
            $"DTSTART:{Format(start)}",
            $"DTEND:{Format(end)}",
            $"SUMMARY:{Escape(evt.Title)}"
        };
        if (isInvite)
        {
            lines.Add($"ORGANIZER;CN={Param(organiserName ?? organiserEmail!)}:mailto:{organiserEmail!.Trim()}");
            lines.Add($"ATTENDEE;CN={Param(attendeeName ?? attendeeEmail!)};ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:{attendeeEmail!.Trim()}");
            lines.Add("SEQUENCE:0");
            lines.Add("STATUS:CONFIRMED");
            lines.Add("TRANSP:OPAQUE");
        }
        if (location.Length > 0) lines.Add($"LOCATION:{Escape(location)}");
        if (details.Length > 0) lines.Add($"DESCRIPTION:{Escape(details.ToString().TrimEnd())}");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        return string.Concat(lines.Select(l => Fold(l) + "\r\n"));
    }

    private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

    // Parameter values can't contain quotes, colons, semicolons or commas unquoted
    private static string Param(string value) => $"\"{value.Replace("\"", "'")}\"";

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
