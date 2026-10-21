using System.Text.RegularExpressions;
using MeetupOrganiser.Core.Entities;

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
        ("TalkDescription", "Current talk's description, in third person; first talk outside a talk block"),
        ("SpeakerBio", "Current speaker's bio; first speaker outside a speaker block"),
        ("SpeakerFirstName", "Current speaker's first name; first speaker outside a speaker block"),
        ("ContactUrl", "Current contact URL, available inside contact link blocks"),
        ("SpeakerLinks", "Formatted contact links for the current speaker"),
        ("SpeakerContacts", "Contact heading and links for the current speaker, blank without links"),
    ];

    public static readonly IReadOnlyList<(string Name, string Description, string Markdown)> Blocks =
    [
        ("TalkBlock", "Repeated for each talk in Talks", "{{TalkTitle}} - {{SpeakerName}}\n\n{{TalkDescription}}"),
        ("TalkDescriptionBlock", "Repeated for each non-empty talk description in TalkDescriptions", "{{TalkDescription}}"),
        ("SingleSpeakerHeading", "Heading when there is one speaker", "Our Speaker:"),
        ("MultipleSpeakersHeading", "Heading when there is more than one speaker", "Our Speakers:"),
        ("SpeakerBlock", "Repeated for each distinct speaker in SpeakerSection", "{{SpeakerName}}:\n\n{{SpeakerBio}}\n\n{{SpeakerContacts}}"),
        ("SpeakerContactsBlock", "Shown only when the current speaker has contact links", "How to find {{SpeakerFirstName}}:\n\n{{SpeakerLinks}}"),
        ("BlueskyLink", "One Bluesky contact link", "Bluesky: {{ContactUrl}}"),
        ("XLink", "One X contact link", "X: {{ContactUrl}}"),
        ("LinkedInLink", "One LinkedIn contact link", "LinkedIn: {{ContactUrl}}"),
        ("GitHubLink", "One GitHub contact link", "GitHub: {{ContactUrl}}"),
        ("FacebookLink", "One Facebook contact link", "Facebook: {{ContactUrl}}"),
        ("PersonalSiteLink", "One personal website contact link", "Personal Site: {{ContactUrl}}"),
        ("ZoomLinkBlock", "Shown only when the event has a Zoom URL", "Online on Zoom: {{ZoomUrl}}"),
        ("ZoomPasscodeBlock", "Shown only when the Zoom meeting has a passcode", "Passcode: {{ZoomPasscode}}"),
        ("GuestSpeakerName", "Fallback when a talk has no speaker", "Guest Speaker"),
        ("DefaultLocation", "Fallback when neither event nor group has a location", "Online on Zoom"),
        ("DefaultGroupName", "Fallback when an event has no group", ".NET Notts")
    ];

    public static List<TemplateSection> CreateDefaultSections() =>
        Placeholders.Select(p => (p.Name, p.Description, Markdown: "{{Value}}"))
            .Concat(Blocks)
            .Select((definition, index) => new TemplateSection
            {
                Id = new Guid($"b4fd713a-0bee-4f49-8790-{index + 1:000000000000}"),
                Name = definition.Name,
                Description = definition.Description,
                Markdown = definition.Markdown
            }).ToList();

    public static bool IsReservedName(string name) =>
        name.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
        Placeholders.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
        Blocks.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces {{Name}} tokens (case-insensitive); unknown tokens are left untouched so typos are visible.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

        rendered = BlankLinesRegex().Replace(rendered.Replace("\r\n", "\n"), "\n\n");
        return rendered.Trim() + "\n";
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> values, IReadOnlyList<TemplateSection>? sections)
    {
        if (sections is null) return Render(template, values);
        ValidateSections(sections);
        var definitions = sections.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Resolve(string name)
        {
            if (resolved.TryGetValue(name, out var cached)) return cached;
            if (!definitions.TryGetValue(name, out var section)) return "{{" + name + "}}";
            var result = Expand(section.Markdown, match =>
                match.Groups[1].Value.Equals("Value", StringComparison.OrdinalIgnoreCase)
                    ? values.FirstOrDefault(v => v.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value ?? ""
                    : Resolve(match.Groups[1].Value));
            resolved[name] = result;
            return result;
        }
        return Render(Expand(template, match => Resolve(match.Groups[1].Value)),
            new Dictionary<string, string>());
    }

    private static string Expand(string text, Func<Match, string> replacement)
    {
        var result = new System.Text.StringBuilder();
        var position = 0;
        void Append(string value)
        {
            if (result.Length + value.Length > 60000)
                throw new ArgumentException("Expanded template content exceeds 60,000 characters. Shorten the template or its sections.");
            result.Append(value);
        }
        foreach (Match match in PlaceholderRegex().Matches(text))
        {
            Append(text[position..match.Index]);
            Append(replacement(match));
            position = match.Index + match.Length;
        }
        Append(text[position..]);
        return result.ToString();
    }

    public static void ValidateSections(IReadOnlyList<TemplateSection> sections)
    {
        if (sections.Count > 100) throw new ArgumentException("A maximum of 100 template sections is supported.");
        var definitions = new Dictionary<string, TemplateSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            if (!Regex.IsMatch(section.Name, @"\A[A-Za-z][A-Za-z0-9_]{0,63}\z") ||
                section.Name.Equals("Value", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Section names must start with a letter, contain only letters, digits or underscores, and be at most 64 characters. Value is reserved.");
            if (!definitions.TryAdd(section.Name, section))
                throw new ArgumentException($"A section named '{section.Name}' already exists.");
            if (section.Markdown.Length > 60000 || section.Description.Length > 500)
                throw new ArgumentException("Section content must be at most 60,000 characters and description at most 500.");
        }
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            if (visited.Contains(name)) return;
            if (!active.Add(name)) throw new ArgumentException($"Template sections contain a circular reference at '{name}'.");
            foreach (Match match in PlaceholderRegex().Matches(definitions[name].Markdown))
            {
                var dependency = match.Groups[1].Value;
                if (dependency.Equals("Value", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Placeholders.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        throw new ArgumentException($"Custom section '{name}' has no automatic Value. Enter content or reference another section.");
                    continue;
                }
                if (!definitions.ContainsKey(dependency))
                    throw new ArgumentException($"Section '{name}' references missing section '{dependency}'. Update that reference first.");
                Visit(dependency);
            }
            active.Remove(name);
            visited.Add(name);
        }
        foreach (var name in definitions.Keys) Visit(name);
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();
}
