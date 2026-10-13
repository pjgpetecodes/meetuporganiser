using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class SpeakerBio
{
    public const string SettingKey = "Integrations:Bio:Preference";
    public const string ThirdPerson = "ThirdPerson";
    public const string Original = "Original";

    public static bool UseOriginal(string? preference) =>
        string.Equals(preference, Original, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the bio to publish, honouring the preference.</summary>
    public static string? Resolve(Speaker speaker, string? preference = null)
    {
        if (UseOriginal(preference))
            return speaker.Bio;

        if (!string.IsNullOrWhiteSpace(speaker.BioThirdPerson))
            return speaker.BioThirdPerson;

        return string.IsNullOrWhiteSpace(speaker.Bio)
            ? speaker.Bio
            : ThirdPersonBioConverter.Convert(speaker.Bio, speaker.Name, speaker.Pronouns);
    }

    /// <summary>Returns a talk description in third person unless the original is preferred.</summary>
    public static string? ResolveTalkDescription(string? description, Speaker? speaker, string? preference = null)
    {
        if (string.IsNullOrWhiteSpace(description) || speaker == null || UseOriginal(preference))
            return description;

        return ThirdPersonBioConverter.Convert(description, speaker.Name, speaker.Pronouns);
    }

    /// <summary>Generates and stores the third-person bio when it is blank, or always when <paramref name="force"/> is set.</summary>
    public static void EnsureThirdPerson(Speaker speaker, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(speaker.Bio))
        {
            if (force) speaker.BioThirdPerson = null;
            return;
        }

        if (force || string.IsNullOrWhiteSpace(speaker.BioThirdPerson))
            speaker.BioThirdPerson = ThirdPersonBioConverter.Convert(speaker.Bio, speaker.Name, speaker.Pronouns);
    }
}
