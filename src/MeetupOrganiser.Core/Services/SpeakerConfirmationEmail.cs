using System.Net;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class SpeakerConfirmationEmail
{
    public static (string Subject, string HtmlBody) Build(string speakerName, string? talkTitle, MeetupEvent evt)
    {
        var name = string.IsNullOrWhiteSpace(speakerName) ? "there" : speakerName.Trim();
        var talk = string.IsNullOrWhiteSpace(talkTitle) ? null : talkTitle.Trim();
        var when = EventHeader.ToUkTime(evt.EventDateTimeUtc).ToString("dddd d MMMM yyyy 'at' HH:mm") + " (UK time)";
        var where = string.Join(", ", new[] { evt.VenueName, evt.LocationOrPlatform }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var rows = new List<string>
        {
            $"<li><strong>Event:</strong> {WebUtility.HtmlEncode(evt.Title)}</li>",
            $"<li><strong>When:</strong> {WebUtility.HtmlEncode(when)}</li>"
        };
        if (where.Length > 0) rows.Add($"<li><strong>Where:</strong> {WebUtility.HtmlEncode(where)}</li>");
        if (talk != null) rows.Add($"<li><strong>Your talk:</strong> {WebUtility.HtmlEncode(talk)}</li>");
        if (!string.IsNullOrWhiteSpace(evt.ZoomMeetingUrl))
            rows.Add($"<li><strong>Zoom:</strong> <a href=\"{WebUtility.HtmlEncode(evt.ZoomMeetingUrl)}\">{WebUtility.HtmlEncode(evt.ZoomMeetingUrl)}</a></li>");

        var html = $"""
            <p>Hi {WebUtility.HtmlEncode(name)},</p>
            <p>Thanks for completing your speaker details. Here's a summary of the meetup:</p>
            <ul>{string.Concat(rows)}</ul>
            <p>A calendar invite is attached so you can add it to your diary. If it doesn't show Accept / Decline buttons, add it here instead:
              <a href="{WebUtility.HtmlEncode(CalendarLinks.Google(evt))}">Google Calendar</a> |
              <a href="{WebUtility.HtmlEncode(CalendarLinks.Outlook(evt))}">Outlook.com</a>
            </p>
            """;

        return ($"Confirmed: {evt.Title}", html);
    }
}
