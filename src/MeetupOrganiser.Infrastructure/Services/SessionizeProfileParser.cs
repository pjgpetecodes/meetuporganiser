using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MeetupOrganiser.Core.DTOs;

namespace MeetupOrganiser.Infrastructure.Services;

public static class SessionizeProfileParser
{
    private static readonly HtmlParser Parser = new();

    public static List<SessionizeProfileCandidate> ParseSearchResults(string html)
    {
        var document = Parser.ParseDocument(html);
        var results = new List<SessionizeProfileCandidate>();

        foreach (var entry in document.QuerySelectorAll(".c-entry--speaker"))
        {
            var anchor = entry.QuerySelector(".c-entry__title a");
            var slug = SessionizeProfileUrl.TryGetSlug(anchor?.GetAttribute("href"));
            var name = Clean(anchor?.TextContent);
            if (slug == null || string.IsNullOrEmpty(name)) continue;

            results.Add(new SessionizeProfileCandidate(
                slug,
                name,
                NullIfEmpty(Clean(entry.QuerySelector(".c-entry__tagline")?.TextContent)),
                SafeHttps(entry.QuerySelector(".c-entry__media img")?.GetAttribute("src"))));
        }

        return results.DistinctBy(r => r.Slug).ToList();
    }

    public static SessionizeProfile? ParseProfile(string html, string slug)
    {
        var document = Parser.ParseDocument(html);
        var name = Clean(document.QuerySelector(".c-s-speaker-info__name")?.TextContent);
        if (string.IsNullOrEmpty(name)) return null;

        var photo = SafeHttps(document.QuerySelector(".c-s-speaker-info__avatar img")?.GetAttribute("src"))
            ?? SafeHttps(document.QuerySelector("meta[property='og:image']")?.GetAttribute("content"));

        var links = new List<SessionizeLinkDto>();
        foreach (var anchor in document.QuerySelectorAll(".c-s-speaker-info__group--links a.c-s-links__link"))
        {
            var href = anchor.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href))
            {
                links.Add(new SessionizeLinkDto(Clean(anchor.TextContent), href, null));
            }
        }

        var sessions = new List<SessionizeProfileSession>();
        foreach (var session in document.QuerySelectorAll(".c-s-session"))
        {
            var titleAnchor = session.QuerySelector(".c-s-session__title a");
            var title = Clean(titleAnchor?.TextContent);
            if (string.IsNullOrEmpty(title)) continue;

            var href = titleAnchor?.GetAttribute("href");
            sessions.Add(new SessionizeProfileSession(
                title,
                NullIfEmpty(ReadText(session.QuerySelector(".c-s-session__summary"))),
                href != null && href.StartsWith('/') ? SessionizeProfileUrl.BaseUrl + href : null));
        }

        return new SessionizeProfile(
            slug,
            $"{SessionizeProfileUrl.BaseUrl}/{slug}",
            name,
            NullIfEmpty(Clean(document.QuerySelector(".c-s-speaker-info__tagline")?.TextContent)),
            NullIfEmpty(Clean(document.QuerySelector(".c-s-speaker-info__location")?.TextContent)),
            NullIfEmpty(ReadText(document.QuerySelector(".c-s-speaker-info__bio"))),
            photo,
            links,
            sessions);
    }

    private static string ReadText(IElement? element)
    {
        if (element == null) return string.Empty;

        foreach (var br in element.QuerySelectorAll("br"))
        {
            br.ReplaceWith(element.Owner!.CreateTextNode("\n"));
        }

        return string.Join("\n\n", (element.TextContent ?? string.Empty)
            .Replace("\r", string.Empty)
            .Split("\n\n", StringSplitOptions.None)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0));
    }

    private static string Clean(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? SafeHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
}
