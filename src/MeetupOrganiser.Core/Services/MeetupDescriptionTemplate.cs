using System.Text.RegularExpressions;

namespace MeetupOrganiser.Core.Services;

public static partial class MeetupDescriptionTemplate
{
    public const string SettingKey = "Integrations:Meetup:DescriptionTemplate";

    public const string Default = """
        # {{EventTitle}}

        {{EventDescription}}

        {{TalkDescriptions}}

        {{SpeakerSection}}

        How to find us:

        We'll be in person at CGI on King Street Nottingham opposite the Brian Clough Statue, and live on Zoom from {{EventTime}} UK Time.

        {{ZoomSection}}
        """;

    /// <summary>Placeholders available to templates, with a short description for the settings UI.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> Placeholders =
    [
        ("EventTitle", "The event title"),
        ("EventDate", "Event date, e.g. Tuesday, October 27, 2026"),
        ("EventTime", "Start time in UK time, e.g. 6:30 PM"),
        ("EventDescription", "The event's own description text (blank if empty)"),
        ("TalkTitle", "Title of the first talk"),
        ("SpeakerName", "Name of the first speaker (all speakers joined with \"and\" if more)"),
        ("Talks", "Each talk as \"Title - Speaker\" followed by its third-person description"),
        ("TalkDescriptions", "Just the talk descriptions, in third person"),
        ("SpeakerSection", "\"Our Speaker(s):\" block with each speaker's bio and contact links"),
        ("Location", "Location / platform for the event"),
        ("ZoomUrl", "Zoom join URL"),
        ("ZoomPasscode", "Zoom passcode"),
        ("ZoomSection", "\"Online on Zoom: url\" plus the passcode, blank if there is no Zoom link"),
        ("GroupName", "Meetup group name"),
        ("WebsiteUrl", "Group website"),
        ("Hashtags", "Group default hashtags"),
    ];

    /// <summary>Replaces {{Name}} tokens (case-insensitive); unknown tokens are left untouched so typos are visible.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

        rendered = BlankLinesRegex().Replace(rendered.Replace("\r\n", "\n"), "\n\n");
        return rendered.Trim() + "\n";
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();
}
