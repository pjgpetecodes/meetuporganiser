using System.Security.Cryptography;
using System.Text;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Infrastructure.Services;

public class MagicLinkService : IMagicLinkService
{
    private readonly AppDbContext _context;

    public MagicLinkService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(string rawToken, MagicLinkToken entity)> CreateTokenAsync(
        Guid speakerId,
        Guid sessionId,
        TimeSpan? validity = null,
        CancellationToken cancellationToken = default)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = HashToken(rawToken);

        var tokenEntity = new MagicLinkToken
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenHash,
            SpeakerId = speakerId,
            EventSpeakerSessionId = sessionId,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity ?? TimeSpan.FromDays(14)),
            CreatedAtUtc = DateTime.UtcNow
        };

        await _context.MagicLinkTokens.AddAsync(tokenEntity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return (rawToken, tokenEntity);
    }

    public async Task<MagicLinkToken?> ValidateTokenAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var tokenHash = HashToken(rawToken);
        var token = await _context.MagicLinkTokens
            .Include(t => t.Speaker)
            .Include(t => t.EventSpeakerSession)
                .ThenInclude(s => s!.MeetupEvent)
                    .ThenInclude(e => e!.MeetupGroup)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token == null || !token.IsValid)
        {
            return null;
        }

        return token;
    }

    public async Task<bool> MarkTokenUsedAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var token = await _context.MagicLinkTokens.FindAsync([tokenId], cancellationToken);
        if (token == null) return false;

        token.UsedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
