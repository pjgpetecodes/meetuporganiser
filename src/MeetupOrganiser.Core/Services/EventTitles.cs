using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Services;

public static class EventTitles
{
    public const string PlaceholderSuffix = "(speaker TBC)";

    public static string Placeholder(string? groupName, DateTime eventDateTimeLocal) =>
        $"{(string.IsNullOrWhiteSpace(groupName) ? "Meetup" : groupName)} - {eventDateTimeLocal:d MMM yyyy} {PlaceholderSuffix}";

    public static bool IsPlaceholder(string? title) =>
        title != null && title.EndsWith(PlaceholderSuffix, StringComparison.Ordinal);

    /// <summary>Builds "Talk - Speaker | Talk - Speaker" from sessions that have a talk title; null when none do.</summary>
    public static string? FromSessions(IEnumerable<EventSpeakerSession> sessions)
    {
        var parts = sessions
            .OrderBy(s => s.OrderIndex)
            .Where(s => !string.IsNullOrWhiteSpace(s.TalkTitle))
            .Select(s => $"{s.TalkTitle} - {s.Speaker?.Name}")
            .ToList();
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }
}
