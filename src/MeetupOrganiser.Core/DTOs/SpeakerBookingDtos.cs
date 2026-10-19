using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.DTOs;

public record AvailableMeetupDate(Guid Id, DateTime EventDateTimeUtc, int DurationMinutes,
    EventFormat Format, string? VenueName, string? LocationOrPlatform, int OccupiedPlaces)
{
    public int RemainingPlaces => 2 - OccupiedPlaces;
}

public record PublicCfpGroup(Guid Id, string Name, string? Description);
public record SpeakerBookingInvitation(Guid GroupId, string GroupName, Speaker Speaker,
    bool RequiresEmailVerification, bool IsPublic, bool HasExistingProfile = false);
public record BookingActionResult(bool Success, string Message);
public record SpeakerBookingResult(bool Success, string Message, MeetupEvent? Event = null,
    Speaker? Speaker = null, bool AlreadyBooked = false);
