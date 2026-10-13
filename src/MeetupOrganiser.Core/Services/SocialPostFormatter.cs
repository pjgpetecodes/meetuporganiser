using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

// Per-group template for the X, Bluesky and LinkedIn announcement posts.
public static class SocialPostFormatter
{
    public const string Default = """
        Announcing the next {{GroupName}} meetup!

        We have {{SpeakerMentions}} coming along to talk to us about {{TalkTitles}}.

        ⏰ {{ShortDate}} @ {{ShortTime}}
        🌎 {{Where}}
        🔗 {{ShortUrl}}

        {{Hashtags}}
        """;

    public const string DotNetNottsDefault = """
        Announcing this month's #DotNetNotts!

        We have {{SpeakerMentions}} coming along to talk to us about {{TalkTitles}}.

        ⏰ {{ShortDate}} @ {{ShortTime}}
        🌎 {{Where}}
        🔗 {{ShortUrl}}
        """;

    public static readonly IReadOnlyList<(string Name, string Description)> Placeholders =
    [
        ("GroupName", "Meetup group name"),
        ("EventTitle", "The event title"),
        ("SpeakerNames", "Speaker names, joined with \"and\""),
        ("SpeakerMentions", "@handle for X / Bluesky (falls back to the name; LinkedIn always uses the name)"),
        ("TalkTitles", "Talk titles in quotes, joined with \"and\""),
        ("ShortDate", "e.g. Mon 26th Jan"),
        ("ShortTime", "24-hour UK time, e.g. 18:30"),
        ("Where", "Depends on the event format, e.g. CGI + Zoom, CGI only, or Zoom"),
        ("Venue", "Venue name (event venue or the default)"),
        ("Format", "In Person, Hybrid or Online"),
        ("ShortUrl", "Short link, falling back to the Meetup event link"),
        ("Hashtags", "Group default hashtags"),
    ];

    public static string Render(MeetupEvent meetupEvent, string platform, string? template)
    {
        var group = meetupEvent.MeetupGroup;
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var speakers = sessions
            .Where(s => s.Speaker != null)
            .Select(s => s.Speaker!)
            .DistinctBy(s => s.Id)
            .ToList();
        var uk = EventHeader.ToUkTime(meetupEvent.EventDateTimeUtc);
        var venue = string.IsNullOrWhiteSpace(meetupEvent.VenueName) ? EventHeader.DefaultVenue : meetupEvent.VenueName.Trim();

        var where = meetupEvent.Format switch
        {
            EventFormat.InPerson => venue,
            EventFormat.Online => "Zoom",
            _ => $"{venue} + Zoom"
        };

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GroupName"] = group?.Name ?? ".NET Notts",
            ["EventTitle"] = meetupEvent.Title,
            ["SpeakerNames"] = Join(speakers.Select(s => s.Name)),
            ["SpeakerMentions"] = Join(speakers.Select(s => Mention(s, platform))),
            ["TalkTitles"] = Join(sessions.Select(s => $"\"{s.TalkTitle}\"")),
            ["ShortDate"] = $"{uk:ddd} {uk.Day}{Suffix(uk.Day)} {uk:MMM}",
            ["ShortTime"] = uk.ToString("HH:mm"),
            ["Where"] = where,
            ["Venue"] = venue,
            ["Format"] = EventHeader.FormatLabel(meetupEvent.Format),
            ["ShortUrl"] = FirstNonBlank(meetupEvent.ShortBitlyUrl, meetupEvent.MeetupEventUrl, group?.WebsiteUrl) ?? "https://meetup.com",
            ["Hashtags"] = group?.DefaultHashtags ?? string.Empty
        };

        return MeetupDescriptionTemplate.Render(string.IsNullOrWhiteSpace(template) ? Default : template, values).TrimEnd();
    }

    private static string Mention(Speaker speaker, string platform)
    {
        var handle = platform.ToLowerInvariant() switch
        {
            "twitter" or "x" => SocialLinks.XHandle(speaker.TwitterHandle),
            "bluesky" => SocialLinks.BlueSkyHandle(speaker.BlueSkyHandle) is { } bsky ? "@" + bsky : null,
            _ => null
        };
        return handle ?? speaker.Name;
    }

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1]
        };
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Suffix(int day) => day switch
    {
        11 or 12 or 13 => "th",
        _ => (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
    };
}
