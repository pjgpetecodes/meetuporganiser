using System.Net;

namespace MeetupOrganiser.Core.Services;

public static class SpeakerInviteEmail
{
    public static (string Subject, string HtmlBody) Build(string? speakerName, string eventTitle, DateTime? eventDateUtc, string link)
    {
        var name = string.IsNullOrWhiteSpace(speakerName) || speakerName == Entities.Speaker.PlaceholderName
            ? "there"
            : speakerName.Trim();
        var subject = $"Your speaker details for {eventTitle}";
        var when = eventDateUtc is { } d
            ? $" on {EventHeader.ToUkTime(d):dddd d MMMM yyyy 'at' HH:mm} (UK time)"
            : string.Empty;

        var html = $"""
            <p>Hi {WebUtility.HtmlEncode(name)},</p>
            <p>Thanks for speaking at <strong>{WebUtility.HtmlEncode(eventTitle)}</strong>{WebUtility.HtmlEncode(when)}.</p>
            <p>Please use the link below to create or check your speaker profile and talk details. The link can be used once and expires in 14 days.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Complete your speaker details</a></p>
            <p>If the link doesn't work, copy this address into your browser:<br />{WebUtility.HtmlEncode(link)}</p>
            """;

        return (subject, html);
    }
}
