namespace MeetupOrganiser.Core.Services;

// Turns stored handles or pasted URLs into clean handles and clickable profile URLs.
public static class SocialLinks
{
    public const string BlueSkyBase = "https://bsky.app/profile";
    public const string XBase = "https://x.com";

    public static string? BlueSkyHandle(string? value) => Handle(value, "bsky.app");

    public static string? XHandle(string? value)
    {
        var handle = Handle(value, "x.com", "twitter.com");
        return handle == null ? null : "@" + handle;
    }

    public static string? BlueSkyUrl(string? value) => ProfileUrl(value, BlueSkyBase, "bsky.app");

    public static string? XUrl(string? value) => ProfileUrl(value, XBase, "x.com", "twitter.com");

    public const string GitHubBase = "https://github.com";

    // GitHub value may be a handle, a profile URL or a repo URL.
    public static string? GitHubUrl(string? value) => ProfileUrl(value, GitHubBase, "github.com");

    public static string? GitHubLabel(string? value)
    {
        var url = GitHubUrl(value);
        return url == null ? null : new Uri(url).AbsolutePath.Trim('/');
    }

    public static string? WebUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : null;
    }

    public static bool IsHost(string? url, params string[] hosts)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return hosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ProfileUrl(string? value, string baseUrl, params string[] hosts)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();

        if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return IsHost(text, hosts) ? WebUrl(text) : null;
        }

        var handle = Handle(text, hosts);
        return handle == null ? null : $"{baseUrl}/{Uri.EscapeDataString(handle)}";
    }

    private static string? Handle(string? value, params string[] hosts)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();

        if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !IsHost(text, hosts)) return null;
            text = uri.AbsolutePath.Trim('/').Split('/')[^1];
        }

        text = text.TrimStart('@', '/');
        return text.Length == 0 ? null : text;
    }
}
