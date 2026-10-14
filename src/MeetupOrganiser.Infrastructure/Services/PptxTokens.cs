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
    public static Dictionary<string, string> BuildReplacements(MeetupEvent meetupEvent, bool includeSampleLiterals)
    {
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var session1 = sessions.FirstOrDefault();
        var session2 = sessions.Skip(1).FirstOrDefault();
        var speaker1 = session1?.Speaker;
        var speaker2 = session2?.Speaker;

        var shortUrl = meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "https://meetup.com";

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{{SpeakerName}}", speaker1?.Name ?? "Speaker Name" },
            { "{{Speaker1Name}}", speaker1?.Name ?? "Speaker 1" },
            { "{{Speaker2Name}}", speaker2?.Name ?? "Speaker 2" },
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

        if (!includeSampleLiterals) return replacements;

        // The built-in decks still carry sample text from the original slides.
        var shortUrlNoScheme = shortUrl.Replace("https://", "").Replace("http://", "");
        replacements["Luce Carter"] = speaker1?.Name ?? "Speaker Name";
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
            replacements["Christophe Cr\u00e9mon"] = speaker1?.Name ?? "Speaker 1";
            replacements["Agents of Change: How Agentic AI is Revolutionizing Every Industry"] = session1?.TalkTitle ?? "Talk 1";
        }

        return replacements;
    }

    public static void ApplyToSlide(Slide slide, IReadOnlyDictionary<string, string> replacements)
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
