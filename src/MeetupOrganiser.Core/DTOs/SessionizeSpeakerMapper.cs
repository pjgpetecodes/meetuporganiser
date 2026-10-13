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

        TidySocialFields(speaker);
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

            TidySocialFields(speaker);
        }
    }

    private static void ApplyLink(Speaker speaker, SessionizeLinkDto link)
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

        var url = uri.AbsoluteUri;
        var handle = uri.AbsolutePath.Trim('/').Split('/')[^1];
        var type = link.LinkType?.Trim() ?? string.Empty;

        if (SocialLinks.IsHost(url, "linkedin.com") || type.Equals("LinkedIn", StringComparison.OrdinalIgnoreCase))
        {
            speaker.LinkedInUrl = Truncate(url, 500);
        }
        else if (SocialLinks.IsHost(url, "twitter.com", "x.com") || type.Equals("Twitter", StringComparison.OrdinalIgnoreCase))
        {
            if (handle.Length > 0) speaker.TwitterHandle = Truncate("@" + handle.TrimStart('@'), 100);
        }
        else if (SocialLinks.IsHost(url, "bsky.app") || type.Equals("Bluesky", StringComparison.OrdinalIgnoreCase))
        {
            if (handle.Length > 0) speaker.BlueSkyHandle = Truncate(handle, 100);
        }
        else if (SocialLinks.IsHost(url, "facebook.com", "fb.com") || type.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
        {
            speaker.FacebookUrl = Truncate(url, 500);
        }
        else if (SocialLinks.IsHost(url, "github.com"))
        {
            // Keep the full URL for repos (github.com/user/repo); plain profiles stay as a handle.
            speaker.GitHubHandle = uri.AbsolutePath.Trim('/').Contains('/')
                ? Truncate(url.TrimEnd('/'), 100)
                : handle.Length > 0 ? Truncate(handle, 100) : speaker.GitHubHandle;
        }
        else if (IsWebsiteType(type) && !SocialLinks.IsHost(url, "instagram.com", "youtube.com", "youtu.be", "tiktok.com"))
        {
            // Blog / company / other sites; never overwrite a website that is already set.
            if (string.IsNullOrWhiteSpace(speaker.WebsiteUrl)) speaker.WebsiteUrl = Truncate(url, 500);
        }
    }

    private static bool IsWebsiteType(string type) =>
        type.Length == 0
        || type.Equals("Blog", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Company_Website", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Other", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Website", StringComparison.OrdinalIgnoreCase);

    // Earlier imports filed Facebook under Website and stored Bluesky/X links as handles; correct those.
    public static void TidySocialFields(Speaker speaker)
    {
        if (SocialLinks.IsHost(speaker.WebsiteUrl, "facebook.com", "fb.com"))
        {
            if (string.IsNullOrWhiteSpace(speaker.FacebookUrl)) speaker.FacebookUrl = Truncate(speaker.WebsiteUrl, 500);
            speaker.WebsiteUrl = null;
        }

        if (!string.IsNullOrWhiteSpace(speaker.BlueSkyHandle))
        {
            speaker.BlueSkyHandle = Truncate(SocialLinks.BlueSkyHandle(speaker.BlueSkyHandle) ?? speaker.BlueSkyHandle, 100);
        }

        if (!string.IsNullOrWhiteSpace(speaker.TwitterHandle))
        {
            speaker.TwitterHandle = Truncate(SocialLinks.XHandle(speaker.TwitterHandle) ?? speaker.TwitterHandle, 100);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
