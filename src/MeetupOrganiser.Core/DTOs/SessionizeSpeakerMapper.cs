using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.DTOs;

public static class SessionizeSpeakerMapper
{
    public static void Apply(Speaker speaker, SessionizeSpeakerDto source)
    {
        speaker.Name = Truncate(string.IsNullOrWhiteSpace(source.FullName)
            ? $"{source.FirstName} {source.LastName}".Trim()
            : source.FullName.Trim(), 200) ?? string.Empty;
        speaker.Bio = Truncate(source.Bio?.Trim(), 4000);

        if (Uri.TryCreate(source.ProfilePicture, UriKind.Absolute, out var photo)
            && photo.Scheme == Uri.UriSchemeHttps
            && photo.AbsoluteUri.Length <= 1000)
        {
            speaker.PhotoBlobUrl = photo.AbsoluteUri;
        }

        foreach (var link in source.Links ?? [])
        {
            if (!Uri.TryCreate(link.Url?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
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
