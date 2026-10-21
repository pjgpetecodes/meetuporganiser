namespace MeetupOrganiser.Core.DTOs;

public record MeetupWebsiteHandoff(int Version, Guid SourceEventId, string GroupSlug, string Operation,
    string? EventUrl, string Title, string Description, DateTime ExpiresAtUtc,
    string StartUtc, string StartDate, string StartTime, string EndDate, string EndTime,
    string TimeZone, string TimeZoneLabel, int DurationMinutes, string Format,
    string? VenueName, string? Location, string ImageBase64)
{
    public MeetupWebsiteSpeaker? Speaker { get; init; }
    public int SpeakerCount { get; init; }
    public string? OnlineEventUrl { get; init; }
}

public record MeetupWebsiteSpeaker(string Name, string Bio, string? Facebook, string? Twitter,
    string? LinkedIn, string? Website)
{
    public string? PhotoBase64 { get; init; }
    public string? PhotoError { get; init; }
}
