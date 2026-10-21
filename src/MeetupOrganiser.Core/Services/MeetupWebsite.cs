using System.Globalization;
using System.Text.RegularExpressions;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static partial class MeetupWebsite
{
    public const int MaximumImageBytes = 5 * 1024 * 1024;

    public static MeetupWebsiteHandoff Prepare(MeetupEvent meetup, string title, string? description, string imageBase64,
        string? bioPreference = null)
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
        {
            try
            {
                var savedUrl = meetup.MeetupEventUrl.Trim();
                if (Uri.TryCreate(savedUrl, UriKind.Absolute, out var parsed))
                    savedUrl = new UriBuilder(parsed) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
                eventUrl = ValidateEventUrl(savedUrl, slug).Url;
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("This event's saved Meetup URL is invalid. In Edit Meetup, set the Meetup URL to " +
                    $"https://www.meetup.com/{slug}/events/<event-id>/, or clear it if this is a new Meetup event. " + ex.Message, ex);
            }
        }
        var utc = DateTime.SpecifyKind(meetup.EventDateTimeUtc, DateTimeKind.Utc);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var start = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(utc.AddMinutes(meetup.DurationMinutes), zone);
        var speakers = meetup.Sessions.OrderBy(s => s.OrderIndex).Where(s => s.Speaker is { IsPlaceholder: false })
            .Select(s => s.Speaker!).DistinctBy(s => s.Id).ToList();
        var speaker = speakers.FirstOrDefault();
        return new(1, meetup.Id, slug, eventUrl is null ? "create" : "update", eventUrl,
            title, description, DateTime.UtcNow.AddMinutes(30), utc.ToString("O"),
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end.ToString("HH:mm", CultureInfo.InvariantCulture),
            "Europe/London", zone.IsDaylightSavingTime(utc) ? "BST" : "GMT", meetup.DurationMinutes,
            meetup.Format.ToString(), meetup.VenueName, meetup.LocationOrPlatform, imageBase64)
        {
            OnlineEventUrl = meetup.Format == EventFormat.InPerson ? null : meetup.ZoomMeetingUrl,
            SpeakerCount = speakers.Count,
            Speaker = speaker is null ? null : new(speaker.Name, SpeakerBio.Resolve(speaker, bioPreference) ?? "",
                speaker.FacebookUrl, speaker.TwitterHandle, speaker.LinkedInUrl, OtherProfileUrl(speaker))
        };
    }

    private static string? OtherProfileUrl(Speaker speaker)
    {
        if (!string.IsNullOrWhiteSpace(speaker.WebsiteUrl)) return speaker.WebsiteUrl;
        foreach (var (profile, root) in new[]
        {
            (speaker.BlueSkyHandle, "https://bsky.app/profile/"),
            (speaker.GitHubHandle, "https://github.com/")
        })
        {
            if (string.IsNullOrWhiteSpace(profile)) continue;
            var value = profile.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo)) return value;
                throw new ArgumentException("The speaker's social profile must use an HTTP(S) URL without credentials.");
            }
            var handle = value.TrimStart('@');
            if (!Regex.IsMatch(handle, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z"))
                throw new ArgumentException("The speaker's social profile is not a valid handle or HTTP(S) URL.");
            return root + handle;
        }
        return null;
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
