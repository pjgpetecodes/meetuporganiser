using System.Net;

namespace MeetupOrganiser.Core.Services;

public static class SpeakerSubmittedEmail
{
    public const string OrganiserEmailSettingKey = "Notifications:OrganiserEmail";

    public static (string Subject, string HtmlBody) Build(string speakerName, string? talkTitle, string eventTitle, string editUrl)
    {
        var speaker = string.IsNullOrWhiteSpace(speakerName) ? "A speaker" : speakerName.Trim();
        var talk = string.IsNullOrWhiteSpace(talkTitle) ? "(no talk title)" : talkTitle.Trim();
        var subject = $"{speaker} completed their speaker details for {eventTitle}";

        var html = $"""
            <p><strong>{WebUtility.HtmlEncode(speaker)}</strong> has completed their speaker details via their magic link.</p>
            <ul>
              <li><strong>Talk:</strong> {WebUtility.HtmlEncode(talk)}</li>
              <li><strong>Event:</strong> {WebUtility.HtmlEncode(eventTitle)}</li>
            </ul>
            <p><a href="{WebUtility.HtmlEncode(editUrl)}">Review and edit the event</a></p>
            """;

        return (subject, html);
    }
}
