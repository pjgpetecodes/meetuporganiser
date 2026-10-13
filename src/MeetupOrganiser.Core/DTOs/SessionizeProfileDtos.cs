namespace MeetupOrganiser.Core.DTOs;

public record SessionizeProfileCandidate(string Slug, string Name, string? Tagline, string? PhotoUrl);

public record SessionizeProfileSession(string Title, string? Summary, string? Url);

public record SessionizeProfile(
    string Slug,
    string Url,
    string Name,
    string? Tagline,
    string? Location,
    string? Bio,
    string? PhotoUrl,
    List<SessionizeLinkDto> Links,
    List<SessionizeProfileSession> Sessions);

[Flags]
public enum SessionizeProfileFields
{
    None = 0,
    Bio = 1,
    Photo = 2,
    Links = 4,
    All = Bio | Photo | Links
}

public static class SessionizeProfileUrl
{
    public const string BaseUrl = "https://sessionize.com";

    /// <summary>Accepts a bare slug or a sessionize.com profile URL and returns the slug, or null when invalid.</summary>
    public static string? TryGetSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var value = input.Trim();
        if (value.StartsWith('/') && !value.StartsWith("//"))
        {
            value = value.TrimStart('/').Split('/', '?', '#')[0];
        }
        else if (value.Contains('/') || value.Contains(':') || value.Contains('.'))
        {
            if (!Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !(uri.Host.Equals("sessionize.com", StringComparison.OrdinalIgnoreCase)
                     || uri.Host.Equals("www.sessionize.com", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            value = uri.AbsolutePath.Trim('/').Split('/')[0];
        }

        value = value.ToLowerInvariant();
        return IsValidSlug(value) ? value : null;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "speakers", "speakers-directory", "s", "login", "signup", "playbook", "api", "events", "pricing"
    };

    private static bool IsValidSlug(string slug) =>
        slug.Length is >= 2 and <= 100
        && !Reserved.Contains(slug)
        && slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
        && slug[0] != '-';
}
