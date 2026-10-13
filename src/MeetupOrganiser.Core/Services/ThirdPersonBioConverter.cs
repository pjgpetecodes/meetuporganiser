using System.Text.RegularExpressions;

namespace MeetupOrganiser.Core.Services;

public static class ThirdPersonBioConverter
{
    private sealed record PronounSet(string Subject, string Object, string Possessive, string PossessivePronoun, string Reflexive, bool Plural);

    private static readonly PronounSet She = new("she", "her", "her", "hers", "herself", false);
    private static readonly PronounSet He = new("he", "him", "his", "his", "himself", false);
    private static readonly PronounSet They = new("they", "them", "their", "theirs", "themselves", true);

    private static readonly HashSet<string> Modals = new(StringComparer.OrdinalIgnoreCase)
    {
        "can", "could", "will", "would", "should", "must", "may", "might", "shall", "cannot", "can't", "won't"
    };

    private static readonly HashSet<string> NonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "but", "to", "in", "at", "of", "for", "with", "the", "a", "an", "on", "as", "so",
        "who", "that", "which", "from", "by", "about", "into", "also"
    };

    private const string Adverbs = "also|always|currently|now|recently|really|still|often|never|just|then|previously|mostly|mainly|especially|even|actually|personally";

    private static readonly Regex FirstPersonRegex = new(
        @"\b(I|I['’](m|ve|ll|d))\b|\b(?i:my|myself|mine)\b|\b(?i:for|with|to|about|from|like|of|by) (?i:me)\b",
        RegexOptions.Compiled);

    private static readonly Regex WordRegex = new(
        @"\b(?:(?<w>I['’](?:m|ve|ll|d)|I)\b(?<rest>(?:\s+(?i:" + Adverbs + @"))*\s+(?<verb>[A-Za-z'’]+))?|(?<w>(?i:myself|mine|my|me))\b)",
        RegexOptions.Compiled);

    public static bool IsFirstPerson(string? bio) =>
        !string.IsNullOrWhiteSpace(bio) && FirstPersonRegex.IsMatch(bio);

    public static string Convert(string? bio, string? speakerName, string? pronouns)
    {
        if (string.IsNullOrWhiteSpace(bio) || !IsFirstPerson(bio))
        {
            return bio ?? string.Empty;
        }

        var firstName = GetFirstName(speakerName);
        var pronounSet = ParsePronouns(pronouns, firstName);
        var paragraphs = Regex.Split(bio.Trim(), @"(\r?\n\s*\r?\n)");
        var nameAvailable = firstName.Length > 0;
        for (var index = 0; index < paragraphs.Length; index += 2)
        {
            var sentences = Regex.Split(paragraphs[index], @"(?<=[.!?])(\s+)");
            for (var s = 0; s < sentences.Length; s += 2)
            {
                sentences[s] = ConvertSentence(sentences[s], firstName, pronounSet, ref nameAvailable);
            }

            paragraphs[index] = string.Concat(sentences);
        }

        return string.Concat(paragraphs);
    }

    private static string ConvertSentence(string sentence, string firstName, PronounSet p, ref bool nameAvailable)
    {
        var available = nameAvailable;
        var result = WordRegex.Replace(sentence, match =>
        {
            var word = match.Groups["w"].Value.Replace('’', '\'');
            var rest = match.Groups["rest"].Success ? match.Groups["rest"].Value : string.Empty;
            var verb = match.Groups["verb"].Success ? match.Groups["verb"].Value : string.Empty;

            switch (word.ToLowerInvariant())
            {
                case "myself": return p.Reflexive;
                case "mine": return p.PossessivePronoun;
                case "my": return p.Possessive;
                case "me": return p.Object;
            }

            var useName = available;
            available = false;
            var subject = useName ? firstName : p.Subject;
            var plural = p.Plural && !useName;

            switch (word.ToLowerInvariant())
            {
                case "i'm": return subject + (plural ? " are" : " is") + rest;
                case "i've": return subject + (plural ? " have" : " has") + rest;
                case "i'll": return subject + " will" + rest;
                case "i'd": return subject + " would" + rest;
            }

            if (verb.Length == 0 || NonVerbs.Contains(verb))
            {
                return subject + rest;
            }

            var adverbs = rest[..rest.LastIndexOf(verb, StringComparison.Ordinal)].TrimEnd();
            return subject + adverbs + " " + ConjugateVerb(verb.Replace('’', '\''), plural);
        });

        nameAvailable = available;
        return Regex.Replace(result, @"^(\s*)([a-z])", m => m.Groups[1].Value + char.ToUpperInvariant(m.Groups[2].Value[0]));
    }

    private static string ConjugateVerb(string verb, bool plural)
    {
        var lower = verb.ToLowerInvariant();
        if (lower == "am") return plural ? "are" : "is";
        if (lower is "was" or "were") return plural ? "were" : "was";
        if (Modals.Contains(lower)) return verb;
        if (lower == "don't") return plural ? "don't" : "doesn't";
        if (lower == "haven't") return plural ? "haven't" : "hasn't";
        if (lower == "have") return plural ? "have" : "has";
        if (lower == "do") return plural ? "do" : "does";
        if (lower == "go") return plural ? "go" : "goes";
        if (lower.EndsWith("ed", StringComparison.Ordinal) && lower is not ("need" or "feed" or "speed" or "embed"))
            return verb;
        if (lower.Length > 4 && lower.EndsWith("ing", StringComparison.Ordinal)) return verb;
        if (plural) return verb;
        if (Regex.IsMatch(lower, "(s|x|z|ch|sh|o)$")) return verb + "es";
        if (Regex.IsMatch(lower, "[^aeiou]y$")) return verb[..^1] + "ies";
        return verb + "s";
    }

    private static PronounSet ParsePronouns(string? pronouns, string firstName)
    {
        var value = pronouns?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.StartsWith("she")) return She;
        if (value.StartsWith("he")) return He;
        if (value.StartsWith("they")) return They;

        // No pronouns specified: always use the speaker's name.
        return firstName.Length == 0
            ? They
            : new PronounSet(firstName, firstName, firstName + "'s", firstName + "'s", "themself", false);
    }

    private static string GetFirstName(string? speakerName) =>
        string.IsNullOrWhiteSpace(speakerName)
            ? string.Empty
            : speakerName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
}
