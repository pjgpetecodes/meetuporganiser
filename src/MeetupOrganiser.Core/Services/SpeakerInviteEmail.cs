using System.Net;

namespace MeetupOrganiser.Core.Services;

public static class SpeakerInviteEmail
{
    public static (string Subject, string HtmlBody) BuildCfpVerification(string groupName, string link, bool hasPreferredDate)
    {
        var safeGroupName = groupName.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var encodedLink = WebUtility.HtmlEncode(link);
        return ($"{safeGroupName}: complete your speaker profile and submit your talk", $"""
            <p>Hello,</p>
            <p>You requested a link to speak at <strong>{WebUtility.HtmlEncode(safeGroupName)}</strong> through our Call for Speakers.</p>
            <p><a href="{encodedLink}">Complete your speaker profile and talk details</a></p>
            <p>Opening this personal link confirms your email address and takes you straight to the submission form. You don't need to create an account or enter a password.</p>
            <p>On the form you can:</p>
            <ul>
                <li>Choose an available meetup date.</li>
                <li>Enter or edit your speaker name, biography, photo and profile links.</li>
                <li>Add your talk title and abstract, then confirm your date and submit your talk.</li>
            </ul>
            <p>{(hasPreferredDate ? "Your preferred date is included in this link and will be selected if it is still available. If it has filled, please choose another date." : "You can select your preferred date from the available speaking opportunities on the form.")}</p>
            <p><strong>Your place is not reserved by opening this link.</strong> It is confirmed only after you submit successfully. Dates marked as shared already have one speaker, so you will be sharing the meetup.</p>
            <p>This link expires <strong>30 minutes after it was requested</strong>. If it expires, return to the group's Call for Speakers page to request a new one. Keep this link private; it gives access to your submission.</p>
            <p>If the link doesn't work, copy this address into your browser:<br />{encodedLink}</p>
            <p>If you didn't request this email, you can safely ignore it. No speaking place has been booked.</p>
            """);
    }

    public static (string Subject, string HtmlBody) BuildDateChoice(string? speakerName, string groupName, string link)
    {
        var name = string.IsNullOrWhiteSpace(speakerName) || speakerName == Entities.Speaker.PlaceholderName ? "there" : speakerName.Trim();
        return ($"Choose your speaking date for {groupName}", $"""
            <p>Hi {WebUtility.HtmlEncode(name)},</p>
            <p>You're invited to speak at <strong>{WebUtility.HtmlEncode(groupName)}</strong>.</p>
            <p>Choose an available upcoming date, review your speaker profile and submit your talk. Dates labelled shared already have one speaker.</p>
            <p>No place is reserved until you submit successfully. This personal link can be used once and expires in 14 days.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Choose a date and submit your talk</a></p>
            <p>If the link doesn't work, copy this address into your browser:<br />{WebUtility.HtmlEncode(link)}</p>
            """);
    }

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
