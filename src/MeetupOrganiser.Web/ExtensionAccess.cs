using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Web;

public sealed partial class ExtensionAccess(TimeProvider clock, IDbContextFactory<AppDbContext> factory)
{
    private sealed record Pairing(string Challenge, DateTimeOffset Expires, string? Owner = null);
    private readonly object gate = new();
    private readonly Dictionary<string, Pairing> pairings = [];
    private readonly SemaphoreSlim redemption = new(1, 1);

    public string? Start(string challenge)
    {
        if (!ChallengePattern().IsMatch(challenge)) return null;
        lock (gate)
        {
            Prune();
            if (pairings.Count >= 1000) return null;
            var id = Random();
            pairings[id] = new(challenge, clock.GetUtcNow().AddMinutes(5));
            return id;
        }
    }

    public bool Approve(string id, string owner)
    {
        lock (gate)
        {
            Prune();
            if (!pairings.TryGetValue(id, out var pairing) || pairing.Owner is not null || string.IsNullOrWhiteSpace(owner))
                return false;
            pairings[id] = pairing with { Owner = owner };
            return true;
        }
    }

    public async Task<(string Status, string? Token, DateTimeOffset? ExpiresAt)> RedeemAsync(string id, string verifier,
        CancellationToken cancellationToken = default)
    {
        if (!ChallengePattern().IsMatch(verifier)) return ("invalid", null, null);
        await redemption.WaitAsync(cancellationToken);
        try
        {
            Pairing pairing;
            lock (gate)
            {
                Prune();
                if (!pairings.TryGetValue(id, out var found) ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(found.Challenge),
                        Encoding.ASCII.GetBytes(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))))))
                    return ("invalid", null, null);
                if (found.Owner is null) return ("pending", null, null);
                pairing = found;
                pairings.Remove(id);
            }
            var token = Random();
            var now = clock.GetUtcNow();
            var expires = now.AddDays(30);
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var expired = await db.ExtensionGrants.Where(g => g.ExpiresAtUtc <= now).ToListAsync(cancellationToken);
            db.ExtensionGrants.RemoveRange(expired);
            db.ExtensionGrants.Add(new() { TokenHash = Hash(token), Owner = pairing.Owner!, ExpiresAtUtc = expires });
            await db.SaveChangesAsync(cancellationToken);
            return ("approved", token, expires);
        }
        finally { redemption.Release(); }
    }

    public async Task<string?> OwnerAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!ChallengePattern().IsMatch(token)) return null;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        return await db.ExtensionGrants.AsNoTracking().Where(g => g.TokenHash == hash && g.ExpiresAtUtc > now)
            .Select(g => g.Owner).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RevokeAsync(string owner, CancellationToken cancellationToken = default)
    {
        await redemption.WaitAsync(cancellationToken);
        try
        {
            lock (gate)
                foreach (var key in pairings.Where(p => p.Value.Owner == owner).Select(p => p.Key).ToArray()) pairings.Remove(key);
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var grants = await db.ExtensionGrants.Where(g => g.Owner == owner).ToListAsync(cancellationToken);
            db.ExtensionGrants.RemoveRange(grants);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { redemption.Release(); }
    }

    public async Task RevokeTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var hash = Hash(token);
        var grant = await db.ExtensionGrants.SingleOrDefaultAsync(g => g.TokenHash == hash, cancellationToken);
        if (grant is null) return;
        db.ExtensionGrants.Remove(grant);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var key in pairings.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) pairings.Remove(key);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    [GeneratedRegex(@"\A[A-Za-z0-9_-]{43}\z")]
    private static partial Regex ChallengePattern();
}
