using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Core.DTOs;

public static class SessionizeSpeakerMapper
{
    public static void Apply(Speaker speaker, SessionizeSpeakerDto source)
    {
        speaker.Name = Truncate(string.IsNullOrWhiteSpace(source.FullName)
            ? $"{source.FirstName} {source.LastName}".Trim()
            : source.FullName.Trim(), 200) ?? string.Empty;
        speaker.Bio = Truncate(source.Bio?.Trim(), 4000);

        var pronounAnswer = source.QuestionAnswers?
            .FirstOrDefault(q => q.Question?.Contains("pronoun", StringComparison.OrdinalIgnoreCase) == true)?
            .Answer?.Trim();
        if (!string.IsNullOrWhiteSpace(pronounAnswer))
        {
            speaker.Pronouns = Truncate(pronounAnswer, 50);
        }

        SpeakerBio.EnsureThirdPerson(speaker, force: true);

        if (Uri.TryCreate(source.ProfilePicture, UriKind.Absolute, out var photo)
            && photo.Scheme == Uri.UriSchemeHttps
            && photo.AbsoluteUri.Length <= 1000)
        {
            speaker.PhotoBlobUrl = photo.AbsoluteUri;
        }

        foreach (var link in source.Links ?? [])
        {
            ApplyLink(speaker, link);
        }
    }

    public static void ApplyProfile(Speaker speaker, SessionizeProfile profile, SessionizeProfileFields fields)
    {
        if (string.IsNullOrWhiteSpace(speaker.Name) && !string.IsNullOrWhiteSpace(profile.Name))
        {
            speaker.Name = Truncate(profile.Name.Trim(), 200) ?? string.Empty;
        }

        if (fields.HasFlag(SessionizeProfileFields.Bio) && !string.IsNullOrWhiteSpace(profile.Bio))
        {
            speaker.Bio = Truncate(profile.Bio.Trim(), 4000);
            SpeakerBio.EnsureThirdPerson(speaker, force: true);
        }

        if (fields.HasFlag(SessionizeProfileFields.Photo) && !string.IsNullOrWhiteSpace(profile.PhotoUrl)
            && profile.PhotoUrl.Length <= 1000)
        {
            speaker.PhotoBlobUrl = profile.PhotoUrl;
        }

        if (fields.HasFlag(SessionizeProfileFields.Links))
        {
            foreach (var link in profile.Links)
            {
                ApplyLink(speaker, link);
            }
        }
    }

    private static void ApplyLink(Speaker speaker, SessionizeLinkDto link)
    {
        {
            if (string.Equals(link.LinkType, "Sessionize", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!Uri.TryCreate(link.Url?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            var handle = uri.AbsolutePath.Trim('/').Split('/')[^1];

            if (host.EndsWith("linkedin.com", StringComparison.OrdinalIgnoreCase))
            {
                speaker.LinkedInUrl = Truncate(uri.AbsoluteUri, 500);
            }
            else if (host is "twitter.com" or "x.com")
            {
                if (handle.Length > 0) speaker.TwitterHandle = Truncate("@" + handle, 100);
            }
            else if (host == "bsky.app")
            {
                if (handle.Length > 0) speaker.BlueSkyHandle = Truncate(handle, 100);
            }
            else if (host == "github.com")
            {
                if (handle.Length > 0) speaker.GitHubHandle = Truncate(handle, 100);
            }
            else if (string.IsNullOrWhiteSpace(speaker.WebsiteUrl))
            {
                speaker.WebsiteUrl = Truncate(uri.AbsoluteUri, 500);
            }
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
