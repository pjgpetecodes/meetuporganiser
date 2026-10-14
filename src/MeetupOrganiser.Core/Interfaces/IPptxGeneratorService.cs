using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface IPptxGeneratorService
{
    Task<byte[]> GenerateEventDeckAsync(MeetupEvent meetupEvent, MeetupEvent? nextEvent = null, CancellationToken cancellationToken = default);
}
