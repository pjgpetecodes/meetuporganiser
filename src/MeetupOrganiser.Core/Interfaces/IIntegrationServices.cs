using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface ISessionizeService
{
    Task<List<SessionizeSpeakerDto>> GetSpeakersAsync(string endpointOrEventId, CancellationToken cancellationToken = default);
    Task<List<SessionizeSessionDto>> GetSessionsAsync(string endpointOrEventId, CancellationToken cancellationToken = default);
}

public interface ISessionizeProfileService
{
    Task<List<SessionizeProfileCandidate>> SearchAsync(string name, CancellationToken cancellationToken = default);
    Task<SessionizeProfile?> GetProfileAsync(string urlOrSlug, CancellationToken cancellationToken = default);
}

public interface IZoomService
{
    Task<ZoomMeetingResult> CreateMeetingAsync(string topic, DateTime startTimeUtc, int durationMinutes, string? agenda = null, CancellationToken cancellationToken = default);
}

public interface IMeetupService
{
    Task<MeetupCreationResult> CreateEventAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default);
    Task<MeetupEventImportResult> ImportPastEventsAsync(string urlname, CancellationToken cancellationToken = default);
    Task<int?> GetAttendeeCountAsync(string eventUrl, CancellationToken cancellationToken = default);
    string GenerateMeetupDescription(MeetupEvent meetupEvent, string? bioPreference = null, string? template = null);
}

public interface IMeetupOAuthService
{
    Task<string> CreateAuthorizationUrlAsync(string state, CancellationToken cancellationToken = default);
    Task CompleteAuthorizationAsync(string code, CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public interface ISocialMediaService
{
    Task<SocialPostResult> PostToBlueSkyAsync(string text, byte[]? imageBytes, string? imageMimeType, CancellationToken cancellationToken = default);
    Task<SocialPostResult> PostToTwitterAsync(string text, byte[]? imageBytes, CancellationToken cancellationToken = default);
    Task<SocialPostResult> PostToLinkedInAsync(string text, byte[]? imageBytes, CancellationToken cancellationToken = default);
    string GeneratePlatformPostText(MeetupEvent meetupEvent, string platform);
    string GetPlatformWebIntentUrl(MeetupEvent meetupEvent, string platform);
}
