namespace MeetupOrganiser.Core.DTOs;

public record MeetupWebsiteHandoff(int Version, Guid SourceEventId, string GroupSlug, string Operation,
    string? EventUrl, string Title, string Description, DateTime ExpiresAtUtc,
    string StartUtc, string StartDate, string StartTime, string EndDate, string EndTime,
    string TimeZone, string TimeZoneLabel, int DurationMinutes, string Format,
    string? VenueName, string? Location, string ImageBase64);
