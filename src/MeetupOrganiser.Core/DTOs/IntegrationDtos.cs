namespace MeetupOrganiser.Core.DTOs;

public record SessionizeSpeakerDto(
    string Id,
    string FirstName,
    string LastName,
    string FullName,
    string? Bio,
    string? TagLine,
    string? ProfilePicture,
    List<SessionizeSessionDto> Sessions
);

public record SessionizeSessionDto(
    string Id,
    string Title,
    string? Description,
    DateTime? StartsAt,
    DateTime? EndsAt,
    List<string> SpeakerIds
);

public record ZoomMeetingResult(
    bool Success,
    string? MeetingId,
    string? JoinUrl,
    string? Passcode,
    string? ErrorMessage
);

public record MeetupCreationResult(
    bool Success,
    string? EventId,
    string? EventUrl,
    string? ErrorMessage
);

public record MeetupEventImportSpeaker(string Name, string? Bio, string? TalkTitle = null);

public record MeetupEventImportDto(
    string MeetupEventId,
    string Title,
    string? Description,
    DateTime EventDateTimeUtc,
    int DurationMinutes,
    string? EventUrl,
    string? Location,
    List<MeetupEventImportSpeaker> Speakers
);

public record MeetupEventImportResult(
    bool Success,
    List<MeetupEventImportDto> Events,
    string? ErrorMessage
);

public record SocialPostResult(
    string Platform,
    bool Success,
    string? PostId,
    string? PostUrl,
    string? FallbackShareUrl,
    string FormattedText,
    string? ErrorMessage
);
