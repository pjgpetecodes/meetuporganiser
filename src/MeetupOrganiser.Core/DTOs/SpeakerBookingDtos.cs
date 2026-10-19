using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.DTOs;

public record AvailableMeetupDate(Guid Id, DateTime EventDateTimeUtc, int DurationMinutes,
    EventFormat Format, string? VenueName, string? LocationOrPlatform, int OccupiedPlaces)
{
    public int RemainingPlaces => 2 - OccupiedPlaces;
}

public record PublicPastMeetup(string Title, DateTime EventDateTimeUtc, int DurationMinutes,
    EventFormat Format, string? VenueName, string? LocationOrPlatform, string? MeetupEventUrl);
public record PublicCfpGroup(Guid Id, string Name, string? Description)
{
    public string? LogoUrl { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? DefaultLocation { get; init; }
    public IReadOnlyList<PublicPastMeetup> PreviousEvents { get; init; } = [];
}
public record SpeakerBookingInvitation(Guid GroupId, string GroupName, Speaker Speaker,
    bool RequiresEmailVerification, bool IsPublic, bool HasExistingProfile = false)
{
    public IReadOnlyList<PreviousSpeakerTalk> PreviousTalks { get; init; } = [];
    public string? GroupLogoUrl { get; init; }
    public string? GroupDescription { get; init; }
    public string? GroupWebsiteUrl { get; init; }
    public string? GroupPrimaryColorHex { get; init; }
    public string? GroupSecondaryColorHex { get; init; }
}
public record PreviousSpeakerTalk(string Title, string? Description, string GroupName, DateTime EventDateTimeUtc);
public record BookingActionResult(bool Success, string Message);
public record SpeakerBookingResult(bool Success, string Message, MeetupEvent? Event = null,
    Speaker? Speaker = null, bool AlreadyBooked = false);
