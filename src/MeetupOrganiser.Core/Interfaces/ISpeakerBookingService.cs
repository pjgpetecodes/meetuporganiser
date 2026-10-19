using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface ISpeakerBookingService
{
    Task<IReadOnlyList<AvailableMeetupDate>> GetAvailableDatesAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<PublicCfpGroup?> GetPublicGroupAsync(string key, CancellationToken cancellationToken = default);
    Task<string> CreateInvitationAsync(Guid groupId, Guid speakerId, CancellationToken cancellationToken = default);
    Task<SpeakerBookingInvitation?> GetInvitationAsync(string token, CancellationToken cancellationToken = default);
    Task<BookingActionResult> RequestVerificationAsync(string key, string email, string baseUrl, Guid? preferredDateId = null, CancellationToken cancellationToken = default);
    Task<BookingActionResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
    Task<SpeakerBookingResult> BookAsync(string token, Guid eventId, Speaker profile, string talkTitle, string? talkDescription, CancellationToken cancellationToken = default);
}
