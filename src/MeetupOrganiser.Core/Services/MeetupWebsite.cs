using System.Globalization;
using System.Text.RegularExpressions;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static partial class MeetupWebsite
{
    public const int MaximumImageBytes = 5 * 1024 * 1024;

    public static MeetupWebsiteHandoff Prepare(MeetupEvent meetup, string title, string? description, string imageBase64)
    {
        var slug = meetup.MeetupGroup?.MeetupUrlName;
        if (string.IsNullOrWhiteSpace(slug) || !GroupSlug().IsMatch(slug))
            throw new ArgumentException("Set this group's Meetup URL name in group settings before preparing a website handoff.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 250)
            throw new ArgumentException("Enter a title between 1 and 250 characters.");
        if (string.IsNullOrWhiteSpace(description) || description.Length > 60000)
            throw new ArgumentException("Enter a description between 1 and 60,000 characters.");
        if (meetup.DurationMinutes is < 1 or > 10080)
            throw new ArgumentException("Choose an event duration between one minute and seven days.");
        if (imageBase64.Length > ((MaximumImageBytes + 2) / 3) * 4)
            throw new ArgumentException("The event image must be a PNG no larger than 5 MB.");
        byte[] image;
        try { image = Convert.FromBase64String(imageBase64); }
        catch (FormatException ex) { throw new ArgumentException("Regenerate the event image before preparing a handoff.", ex); }
        if (image.Length > MaximumImageBytes || image.Length < 8 || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("Regenerate a PNG event image no larger than 5 MB.");

        string? eventUrl = null;
        if (!string.IsNullOrWhiteSpace(meetup.MeetupEventUrl))
            eventUrl = ValidateEventUrl(meetup.MeetupEventUrl, slug).Url;
        var utc = DateTime.SpecifyKind(meetup.EventDateTimeUtc, DateTimeKind.Utc);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var start = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(utc.AddMinutes(meetup.DurationMinutes), zone);
        return new(1, meetup.Id, slug, eventUrl is null ? "create" : "update", eventUrl,
            title, description, DateTime.UtcNow.AddMinutes(30), utc.ToString("O"),
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end.ToString("HH:mm", CultureInfo.InvariantCulture),
            "Europe/London", zone.IsDaylightSavingTime(utc) ? "BST" : "GMT", meetup.DurationMinutes,
            meetup.Format.ToString(), meetup.VenueName, meetup.LocationOrPlatform, imageBase64);
    }

    public static (string Url, string Id) ValidateEventUrl(string value, string groupSlug, string? existingUrl = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host is not ("www.meetup.com" or "meetup.com") || !uri.IsDefaultPort ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Enter the HTTPS Meetup event URL without query parameters or fragments.");
        var match = EventPath().Match(uri.AbsolutePath);
        if (!match.Success || !string.Equals(match.Groups[1].Value, groupSlug, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter a real event URL belonging to this Meetup group, not a create/edit page.");
        var id = match.Groups[2].Value;
        if (!string.IsNullOrWhiteSpace(existingUrl) && ValidateEventUrl(existingUrl, groupSlug).Id != id)
            throw new ArgumentException("This event is already linked to a different Meetup event. Update the existing event instead.");
        return ($"https://www.meetup.com/{groupSlug}/events/{id}/", id);
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]*\z")]
    private static partial Regex GroupSlug();
    [GeneratedRegex(@"\A/([A-Za-z0-9][A-Za-z0-9_-]*)/events/([0-9]+)/?\z")]
    private static partial Regex EventPath();
}
