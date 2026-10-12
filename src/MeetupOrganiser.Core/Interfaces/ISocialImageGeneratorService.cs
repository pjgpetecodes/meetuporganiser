using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface ISocialImageGeneratorService
{
    Task<byte[]> GenerateSocialCardAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default);
}
