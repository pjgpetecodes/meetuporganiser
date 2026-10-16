using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Infrastructure.Services;

// Token replacement shared by the deck generator and the template slide previews.
public static partial class PptxTokens
{
    public static Dictionary<string, string> BuildReplacements(MeetupEvent meetupEvent, bool includeSampleLiterals, MeetupEvent? nextEvent = null)
    {
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var session1 = sessions.FirstOrDefault();
        var session2 = sessions.Skip(1).FirstOrDefault();
        var speaker1 = session1?.Speaker;
        var speaker2 = session2?.Speaker;

        var shortUrl = meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "https://meetup.com";

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{{SpeakerName}}", speaker1?.Name ?? "Speaker One" },
            { "{{Speaker1Name}}", speaker1?.Name ?? "Speaker One" },
            { "{{Speaker2Name}}", speaker2?.Name ?? "Speaker Two" },
            { "{{TalkTitle}}", session1?.TalkTitle ?? meetupEvent.Title },
            { "{{Talk1Title}}", session1?.TalkTitle ?? meetupEvent.Title },
            { "{{Talk2Title}}", session2?.TalkTitle ?? "Talk 2" },
            { "{{ShortUrl}}", shortUrl },
            { "{{EventDate}}", meetupEvent.EventDateTimeUtc.ToString("dddd d'th' MMMM yyyy") },
            { "{{EventTime}}", EventHeader.ToUkTime(meetupEvent.EventDateTimeUtc).ToString("HH:mm") + " UK time" },
            { "{{Venue}}", meetupEvent.LocationOrPlatform ?? "Online" },
            { "{{EventDateLine}}", EventHeader.DateLine(meetupEvent) },
            { "{{EventVenueLine}}", EventHeader.VenueLine(meetupEvent) },
            { "{{EventFormat}}", EventHeader.FormatLabel(meetupEvent.Format) },
            { "{{EventSubtitle}}", EventHeader.Subtitle(meetupEvent) ?? string.Empty },
        };

        AddNextEventReplacements(replacements, nextEvent);

        if (!includeSampleLiterals) return replacements;

        // The built-in decks still carry sample text from the original slides.
        var shortUrlNoScheme = shortUrl.Replace("https://", "").Replace("http://", "");
        replacements["Luce Carter"] = speaker1?.Name ?? "Speaker One";
        replacements["SQL to MongoDB API with C#"] = session1?.TalkTitle ?? meetupEvent.Title;
        replacements["https://bit.ly/dotnetnottsapril2026"] = shortUrl;
        replacements["bit.ly/dotnetnottsapril2026"] = shortUrlNoScheme;
        replacements["Monday 27th April 2026"] = meetupEvent.EventDateTimeUtc.ToString("dddd d'th' MMMM yyyy");
        replacements["Monday"] = meetupEvent.EventDateTimeUtc.ToString("dddd");
        replacements["April 2026"] = meetupEvent.EventDateTimeUtc.ToString("MMMM yyyy");

        if (speaker2 != null && session2 != null)
        {
            replacements["Pete Gallagher"] = speaker2.Name;
            replacements["Detecting Pollinating Insects with a Raspberry Pi and the Hailo AI Kit"] = session2.TalkTitle;
            replacements["Christophe Cr\u00e9mon"] = speaker1?.Name ?? "Speaker One";
            replacements["Agents of Change: How Agentic AI is Revolutionizing Every Industry"] = session1?.TalkTitle ?? "Talk 1";
        }

        return replacements;
    }

    private const string NextEventPlaceholder = "TBC";

    // Next-event tokens fall back to "TBC" until the following event has been created.
    private static void AddNextEventReplacements(Dictionary<string, string> replacements, MeetupEvent? next)
    {
        var sessions = next?.Sessions.OrderBy(s => s.OrderIndex).ToList() ?? [];
        var first = sessions.FirstOrDefault();
        var second = sessions.Skip(1).FirstOrDefault();

        string Or(string? value) => string.IsNullOrWhiteSpace(value) ? NextEventPlaceholder : value;

        replacements["{{NextEventTitle}}"] = Or(next?.Title);
        replacements["{{NextEventDate}}"] = next == null ? NextEventPlaceholder : next.EventDateTimeUtc.ToString("dddd d'th' MMMM yyyy");
        replacements["{{NextEventTime}}"] = next == null ? NextEventPlaceholder : EventHeader.ToUkTime(next.EventDateTimeUtc).ToString("HH:mm") + " UK time";
        replacements["{{NextEventDateLine}}"] = next == null ? NextEventPlaceholder : EventHeader.DateLine(next);
        replacements["{{NextEventVenue}}"] = Or(next?.LocationOrPlatform);
        replacements["{{NextEventVenueLine}}"] = next == null ? NextEventPlaceholder : EventHeader.VenueLine(next);
        replacements["{{NextEventFormat}}"] = next == null ? NextEventPlaceholder : EventHeader.FormatLabel(next.Format);
        replacements["{{NextEventSubtitle}}"] = next == null ? string.Empty : EventHeader.Subtitle(next) ?? string.Empty;
        replacements["{{NextEventShortUrl}}"] = Or(next?.ShortBitlyUrl ?? next?.MeetupEventUrl);
        replacements["{{NextEventSpeakerName}}"] = Or(first?.Speaker?.Name);
        replacements["{{NextEventSpeaker1Name}}"] = Or(first?.Speaker?.Name);
        replacements["{{NextEventSpeaker2Name}}"] = Or(second?.Speaker?.Name);
        replacements["{{NextEventSpeakers}}"] = Or(string.Join(" & ", sessions.Select(s => s.Speaker?.Name).Where(n => !string.IsNullOrWhiteSpace(n))));
        replacements["{{NextEventTalkTitle}}"] = Or(first?.TalkTitle);
        replacements["{{NextEventTalk1Title}}"] = Or(first?.TalkTitle);
        replacements["{{NextEventTalk2Title}}"] = Or(second?.TalkTitle);
    }

    private static string SlideText(Slide slide) =>
        string.Concat(slide.Descendants<D.Text>().Select(t => t.Text));

    // The built-in decks' "Upcoming meetups" divider slide and the upcoming-event slide that follows it.
    public static bool IsUpcomingTitleSlide(Slide slide) =>
        SlideText(slide).Replace(" ", "").Equals("Upcomingmeetups", StringComparison.OrdinalIgnoreCase);

    public static bool IsUpcomingEventSlide(Slide slide)
    {
        var text = SlideText(slide);
        return text.Contains("Special Workshop Edition", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("TONIGHT", StringComparison.OrdinalIgnoreCase);
    }

    // Fills the built-in upcoming-event slide paragraph by paragraph, since its sample date is split across runs.
    public static void ApplyUpcomingEventSlide(Slide slide, MeetupEvent next)
    {
        var sessions = next.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var speakers = string.Join(" & ", sessions.Select(s => s.Speaker?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        var talks = string.Join(" / ", sessions.Select(s => s.TalkTitle).Where(n => !string.IsNullOrWhiteSpace(n)));
        var url = next.ShortBitlyUrl ?? next.MeetupEventUrl ?? string.Empty;

        foreach (var paragraph in slide.Descendants<D.Paragraph>().ToList())
        {
            var texts = paragraph.Descendants<D.Text>().ToList();
            if (texts.Count == 0) continue;
            var joined = string.Concat(texts.Select(t => t.Text));

            string? value = null;
            if (joined.Contains("Luce Carter")) value = string.IsNullOrEmpty(speakers) ? NextEventPlaceholder : speakers;
            else if (joined.Contains("SQL to MongoDB")) value = string.IsNullOrEmpty(talks) ? next.Title : talks;
            else if (joined.Contains("bit.ly")) value = url;
            else if (joined.Contains("Monday")) value = EventHeader.DateLine(next);
            else if (joined.Contains("at CGI only")) value = EventHeader.VenueLine(next);
            else if (joined.Contains("Special Workshop")) value = EventHeader.Subtitle(next) ?? string.Empty;
            if (value == null) continue;

            texts[0].Text = value;
            foreach (var extra in texts.Skip(1)) extra.Text = string.Empty;
        }
    }

    // Next-event slides carry their own speaker count: Speaker2/Talk2 tokens mean two speakers, other speaker/talk tokens one.
    public static SlideKind ClassifyNextEvent(IReadOnlyList<string> tokens)
    {
        var names = tokens.Select(t => Normalise(t).Trim('{', '}')).ToList();
        if (names.Any(n => n.Equals("NextEventSpeaker2Name", StringComparison.OrdinalIgnoreCase) || n.Equals("NextEventTalk2Title", StringComparison.OrdinalIgnoreCase))) return SlideKind.TwoSpeakers;
        return names.Any(n => n.StartsWith("NextEventSpeaker", StringComparison.OrdinalIgnoreCase) && !n.Equals("NextEventSpeakers", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("NextEventTalk", StringComparison.OrdinalIgnoreCase)) ? SlideKind.OneSpeaker : SlideKind.Always;
    }

    // True when every token on the slide is a next-event token.
    public static bool UsesOnlyNextEventTokens(IReadOnlyList<string> tokens) =>
        tokens.Count > 0 && tokens.All(t => Normalise(t).StartsWith("{{NextEvent", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] VenueTokens =
        ["{{Venue}}", "{{EventTime}}", "{{EventVenueLine}}", "{{NextEventVenue}}", "{{NextEventTime}}", "{{NextEventVenueLine}}"];

    // Characters that fit on one line of the template's header box at its authored size.
    private const int HeaderLineCapacity = 28;
    private const float MinHeaderScale = 0.5f;

    public static void ApplyToSlide(Slide slide, IReadOnlyDictionary<string, string> replacements)
    {
        var venueParagraphs = slide.Descendants<D.Paragraph>()
            .Where(p => string.Concat(p.Descendants<D.Text>().Select(t => t.Text)) is var t
                && VenueTokens.Any(v => t.Contains(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        ReplaceTokens(slide, replacements);

        foreach (var paragraph in venueParagraphs) FitParagraph(paragraph);
    }

    // Shrinks a header line that is longer than the box can hold, e.g. a long location.
    private static void FitParagraph(D.Paragraph paragraph)
    {
        var length = string.Concat(paragraph.Descendants<D.Text>().Select(t => t.Text)).Trim().Length;
        if (length <= HeaderLineCapacity) return;

        var scale = Math.Max(MinHeaderScale, HeaderLineCapacity / (float)length);
        var sized = false;
        foreach (var rp in paragraph.Descendants<D.RunProperties>().Where(r => r.FontSize?.Value > 0))
        {
            rp.FontSize = (int)(rp.FontSize!.Value * scale);
            sized = true;
        }

        if (sized) return;

        // Size is inherited from the layout, so ask PowerPoint to shrink the text instead.
        if (paragraph.Parent is not TextBody { BodyProperties: { } body }) return;
        if (body.Elements<D.NormalAutoFit>().Any()) return;
        body.RemoveAllChildren<D.NoAutoFit>();
        body.RemoveAllChildren<D.ShapeAutoFit>();
        var fit = new D.NormalAutoFit { FontScale = (int)(scale * 100000) };
        var warp = body.GetFirstChild<D.PresetTextWarp>();
        if (warp != null) warp.InsertAfterSelf(fit); else body.PrependChild(fit);
    }

    private static void ReplaceTokens(Slide slide, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var textElem in slide.Descendants<D.Text>())
        {
            var text = textElem.Text;
            if (string.IsNullOrEmpty(text)) continue;
            textElem.Text = Replace(text, replacements);
        }

        // PowerPoint often splits a typed token across runs; merge those paragraphs into their first run.
        foreach (var paragraph in slide.Descendants<D.Paragraph>())
        {
            var texts = paragraph.Descendants<D.Text>().ToList();
            if (texts.Count < 2) continue;

            var joined = string.Concat(texts.Select(t => t.Text));
            if (!joined.Contains("{{")) continue;

            var replaced = Replace(joined, replacements);
            if (replaced == joined) continue;

            texts[0].Text = replaced;
            foreach (var extra in texts.Skip(1)) extra.Text = string.Empty;
        }
    }

    public enum SlideKind { Always, OneSpeaker, TwoSpeakers }

    private static readonly string[] SecondSpeakerTokens = ["{{Speaker2Name}}", "{{Talk2Title}}"];
    private static readonly string[] FirstSpeakerTokens = ["{{SpeakerName}}", "{{Speaker1Name}}", "{{TalkTitle}}", "{{Talk1Title}}"];

    public static SlideKind Classify(IReadOnlyList<string> tokens)
    {
        if (tokens.Any(t => SecondSpeakerTokens.Contains(Normalise(t), StringComparer.OrdinalIgnoreCase))) return SlideKind.TwoSpeakers;
        return tokens.Any(t => FirstSpeakerTokens.Contains(Normalise(t), StringComparer.OrdinalIgnoreCase)) ? SlideKind.OneSpeaker : SlideKind.Always;
    }

    private static readonly string[] DateTokens = ["{{EventDate}}", "{{EventDateLine}}"];

    // The "Social Images" divider and the date slides after it are only for generating social cards, so the deck hides them.
    public static HashSet<int> FindSocialSection(IReadOnlyList<(string Text, IReadOnlyList<string> Tokens)> slides)
    {
        var result = new HashSet<int>();
        var start = -1;
        for (var i = 0; i < slides.Count && start < 0; i++)
        {
            if (slides[i].Tokens.Count == 0 && slides[i].Text.Replace(" ", "").Equals("SocialImages", StringComparison.OrdinalIgnoreCase)) start = i;
        }

        if (start < 0) return result;

        result.Add(start);
        for (var i = start + 1; i < slides.Count; i++)
        {
            if (slides[i].Tokens.Any(t => DateTokens.Contains(Normalise(t), StringComparer.OrdinalIgnoreCase))) result.Add(i);
        }

        return result;
    }

    public static string TextOf(Slide slide) => SlideText(slide);

    // The social slide is the one showing this event's date; prefer the one matching the speaker count and carrying speaker details.
    public static int? FindSocialSlide(IReadOnlyList<(int Number, IReadOnlyList<string> Tokens)> slides, int speakerCount)
    {
        var wanted = speakerCount > 1 ? SlideKind.TwoSpeakers : SlideKind.OneSpeaker;
        var candidates = slides
            .Where(s => s.Tokens.Any(t => DateTokens.Contains(Normalise(t), StringComparer.OrdinalIgnoreCase)))
            .Select(s => (s.Number, Kind: Classify(s.Tokens)))
            .ToList();

        return candidates.Where(c => c.Kind == wanted).Select(c => (int?)c.Number).FirstOrDefault()
            ?? candidates.Where(c => c.Kind != (wanted == SlideKind.OneSpeaker ? SlideKind.TwoSpeakers : SlideKind.OneSpeaker)).Select(c => (int?)c.Number).FirstOrDefault()
            ?? candidates.Select(c => (int?)c.Number).FirstOrDefault();
    }

    private static string Normalise(string token) => "{{" + token.Trim('{', '}').Trim() + "}}";

    public static IReadOnlyList<string> DetectTokens(Slide slide)
    {
        var tokens = new List<string>();
        foreach (var paragraph in slide.Descendants<D.Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<D.Text>().Select(t => t.Text));
            foreach (Match match in TokenRegex().Matches(text))
            {
                var token = "{{" + match.Groups[1].Value + "}}";
                if (!tokens.Contains(token, StringComparer.OrdinalIgnoreCase)) tokens.Add(token);
            }
        }

        return tokens;
    }

    private static string Replace(string text, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var (key, value) in replacements)
        {
            if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace(key, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex TokenRegex();
}
