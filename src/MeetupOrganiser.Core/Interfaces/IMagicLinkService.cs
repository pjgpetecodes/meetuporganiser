using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface IMagicLinkService
{
    Task<(string rawToken, MagicLinkToken entity)> CreateTokenAsync(Guid speakerId, Guid sessionId, TimeSpan? validity = null, CancellationToken cancellationToken = default);
    Task<MagicLinkToken?> ValidateTokenAsync(string rawToken, CancellationToken cancellationToken = default);
    Task<bool> MarkTokenUsedAsync(Guid tokenId, CancellationToken cancellationToken = default);
}
