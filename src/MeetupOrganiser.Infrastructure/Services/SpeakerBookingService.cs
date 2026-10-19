using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public sealed class SpeakerBookingService(
    IDbContextFactory<AppDbContext> contextFactory,
    IEmailService emailService,
    SpeakerPhotoInterceptor? photoInterceptor = null,
    ILogger<SpeakerBookingService>? logger = null) : ISpeakerBookingService
{
    private const string InvalidLink = "This link is invalid, expired, or no longer available.";

    public async Task<IReadOnlyList<AvailableMeetupDate>> GetAvailableDatesAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        return await db.MeetupEvents.AsNoTracking()
            .Where(e => e.MeetupGroupId == groupId && e.Status == EventStatus.Draft &&
                e.IsOpenForBookings && e.EventDateTimeUtc > now && e.Sessions.Count < 2)
            .OrderBy(e => e.EventDateTimeUtc)
            .Select(e => new AvailableMeetupDate(e.Id, e.EventDateTimeUtc, e.DurationMinutes,
                e.Format, e.VenueName, e.LocationOrPlatform, e.Sessions.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<PublicCfpGroup?> GetPublicGroupAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.MeetupGroups.AsNoTracking().Where(g => g.CfpEnabled && g.CfpKey == key)
            .Select(g => new PublicCfpGroup(g.Id, g.Name, g.Description)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string> CreateInvitationAsync(Guid groupId, Guid speakerId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MeetupGroups.AnyAsync(g => g.Id == groupId, cancellationToken) ||
            !await db.Speakers.AnyAsync(s => s.Id == speakerId, cancellationToken))
            throw new ArgumentException("The group and speaker must exist.");
        var raw = NewToken();
        db.SpeakerBookingTokens.Add(new SpeakerBookingToken
        {
            MeetupGroupId = groupId, SpeakerId = speakerId,
            TokenHash = Hash(raw), ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
        });
        await db.SaveChangesAsync(cancellationToken);
        return raw;
    }

    public async Task<SpeakerBookingInvitation?> GetInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var proof = await FindProofAsync(db, token, cancellationToken);
        if (!Valid(proof) || proof!.UsedAtUtc is not null) return null;
        var isPublic = proof.SpeakerId is null;
        var matched = isPublic && proof.VerifiedAtUtc is not null
            ? await FindVerifiedSpeakerAsync(db, proof.Email!, cancellationToken) : null;
        var profile = isPublic
            ? matched is null ? new Speaker() : CopyProfile(matched)
            : proof.Speaker!;
        if (isPublic) profile.Email = proof.VerifiedAtUtc is not null ? proof.Email : null;
        return new(proof.MeetupGroupId, proof.MeetupGroup!.Name,
            profile, isPublic && proof.VerifiedAtUtc is null, isPublic, !isPublic || matched is not null);
    }

    public async Task<BookingActionResult> RequestVerificationAsync(string key, string email, string baseUrl, Guid? preferredDateId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return new(false, InvalidLink);
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 250 || !new EmailAddressAttribute().IsValid(normalized))
            return new(false, "Enter a valid email address.");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.Scheme is not ("https" or "http"))
            return new(false, "The verification address is not configured.");

        var raw = NewToken();
        var proofId = Guid.NewGuid();
        var groupName = string.Empty;
        var result = await InTransactionAsync(async db =>
        {
            var groupId = await db.MeetupGroups.Where(g => g.CfpEnabled && g.CfpKey == key)
                .Select(g => (Guid?)g.Id).SingleOrDefaultAsync(cancellationToken);
            if (groupId is null) return new BookingActionResult(false, InvalidLink);
            await LockGroupAsync(db, groupId.Value, cancellationToken);
            var group = await db.MeetupGroups.AsNoTracking().SingleAsync(g => g.Id == groupId, cancellationToken);
            if (!group.CfpEnabled || group.CfpKey != key) return new(false, InvalidLink);
            groupName = group.Name;
            if (await db.SpeakerBookingTokens.AnyAsync(t => t.Id == proofId, cancellationToken))
                return new(true, "Verification requested.");
            var since = DateTime.UtcNow.AddHours(-1);
            var requests = db.SpeakerBookingTokens.Where(t => t.MeetupGroupId == group.Id && t.SpeakerId == null && t.CreatedAtUtc > since);
            if (await requests.CountAsync(cancellationToken) >= 100 ||
                await requests.CountAsync(t => t.Email == normalized, cancellationToken) >= 5)
                return new(false, "Too many requests. Please try again later.");
            db.SpeakerBookingTokens.Add(new SpeakerBookingToken
            {
                Id = proofId, MeetupGroupId = group.Id, Email = normalized, PublicKey = key,
                TokenHash = Hash(raw), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(true, "Verification requested.");
        }, cancellationToken);
        if (!result.Success) return result;

        var url = $"{baseUrl.TrimEnd('/')}/speaker/choose/{raw}";
        if (preferredDateId is { } dateId) url += $"?dateId={dateId}";
        bool delivered;
        try
        {
            var emailContent = SpeakerInviteEmail.BuildCfpVerification(groupName, url, preferredDateId.HasValue);
            delivered = await emailService.SendAsync(normalized!, emailContent.Subject, emailContent.HtmlBody,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RevokeAsync(proofId, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning("Verification email delivery failed.");
            await RevokeAsync(proofId, CancellationToken.None);
            return new(false, "Verification email could not be delivered. Please try again.");
        }
        if (!delivered)
        {
            logger?.LogWarning("Verification email delivery was unavailable.");
            await RevokeAsync(proofId, CancellationToken.None);
            return new(false, "Verification email could not be delivered. Please try again.");
        }
        return new(true, "Check your email for a verification link.");
    }

    public Task<BookingActionResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async db =>
        {
            var proof = await LockProofAsync(db, token ?? string.Empty, cancellationToken);
            if (!Valid(proof) || proof!.SpeakerId is not null || proof.UsedAtUtc is not null)
                return new BookingActionResult(false, InvalidLink);
            if (proof.VerifiedAtUtc is not null)
                return new(true, "This email has already been verified. You can choose a date.");
            proof.VerifiedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new(true, "Email verified. Choose a date to complete your booking.");
        }, cancellationToken);

    public async Task<SpeakerBookingResult> BookAsync(string token, Guid eventId, Speaker profile, string talkTitle,
        string? talkDescription, CancellationToken cancellationToken = default)
    {
        var safeProfile = CopyProfile(profile);
        await using (var initial = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var proof = await FindProofAsync(initial, token ?? string.Empty, cancellationToken);
            if (!Valid(proof, allowUsed: true)) return new(false, InvalidLink);
            if (proof!.UsedAtUtc is not null)
                return await InTransactionAsync(async db =>
                {
                    var locked = await LockProofAsync(db, token ?? string.Empty, cancellationToken);
                    return Valid(locked, allowUsed: true) ? await PreviousBookingAsync(db, locked!, cancellationToken) :
                        new SpeakerBookingResult(false, InvalidLink);
                }, cancellationToken);
            if (proof.SpeakerId is null && proof.VerifiedAtUtc is null)
                return new(false, "Verify this email address before booking.");
            var original = proof.SpeakerId is null
                ? await FindVerifiedSpeakerAsync(initial, proof.Email!, cancellationToken) : proof.Speaker;
            PrepareBio(safeProfile, original);
        }
        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(safeProfile, new ValidationContext(safeProfile), validation, true) ||
            string.IsNullOrWhiteSpace(safeProfile.Name) || string.IsNullOrWhiteSpace(safeProfile.Email))
            return new(false, validation.FirstOrDefault()?.ErrorMessage ?? "Name and email are required.");
        if (string.IsNullOrWhiteSpace(talkTitle) || talkTitle.Length > 300 || talkDescription?.Length > 6000)
            return new(false, "A talk title of up to 300 characters and an abstract of up to 6000 characters are required.");
        if (safeProfile.BioThirdPerson?.Length > 4000)
            return new(false, "The third-person biography must be at most 4000 characters.");

        // Upload outside the transaction; no network I/O while holding event/proof locks.
        if (photoInterceptor is not null)
        {
            await using var photos = await contextFactory.CreateDbContextAsync(cancellationToken);
            photos.Speakers.Add(safeProfile);
            await photoInterceptor.LocalizeAsync(photos, cancellationToken);
        }
        return await InTransactionAsync(async db =>
        {
            db.SkipSpeakerPhotoLocalization = true;
            var proof = await LockProofAsync(db, token ?? string.Empty, cancellationToken);
            if (!Valid(proof, allowUsed: true)) return new SpeakerBookingResult(false, InvalidLink);
            if (proof!.UsedAtUtc is not null)
                return await PreviousBookingAsync(db, proof, cancellationToken);
            var isPublic = proof.SpeakerId is null;
            if (isPublic && (proof.VerifiedAtUtc is null ||
                !string.Equals(safeProfile.Email?.Trim(), proof.Email, StringComparison.OrdinalIgnoreCase)))
                return new(false, "Verify this email address before booking.");
            await db.LockEventAsync(eventId, cancellationToken);
            var evt = await db.MeetupEvents.Include(e => e.MeetupGroup)
                .Include(e => e.Sessions).ThenInclude(s => s.Speaker)
                .SingleOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (evt is null || evt.MeetupGroupId != proof.MeetupGroupId || !evt.IsOpenForBookings ||
                evt.Status != EventStatus.Draft || evt.EventDateTimeUtc <= DateTime.UtcNow || evt.Sessions.Count >= 2)
                return new(false, "This date is no longer available. Please choose another date.");
            if (!isPublic && evt.Sessions.Any(s => s.SpeakerId == proof.SpeakerId))
                return new(false, "You are already assigned to this date.");
            if (isPublic && evt.Sessions.Any(s =>
                string.Equals(s.Speaker?.Email?.Trim(), proof.Email, StringComparison.OrdinalIgnoreCase)))
                return new(false, "This email address already has a booking on this date. Please choose another date.");
            var previousGeneratedTitle = EventTitles.FromSessions(evt.Sessions);
            var shouldGenerateTitle = string.IsNullOrWhiteSpace(evt.Title) || EventTitles.IsPlaceholder(evt.Title) ||
                (previousGeneratedTitle is not null &&
                 evt.Title == previousGeneratedTitle[..Math.Min(previousGeneratedTitle.Length, 250)]);

            var originalSpeaker = isPublic
                ? await FindVerifiedSpeakerAsync(db, proof.Email!, cancellationToken)
                : await db.Speakers.SingleAsync(s => s.Id == proof.SpeakerId, cancellationToken);
            var speaker = originalSpeaker ?? CopyProfile(safeProfile);
            if (evt.Sessions.Any(s => s.SpeakerId == speaker.Id))
                return new(false, "You are already assigned to this date.");
            PrepareBio(safeProfile, originalSpeaker);
            if (safeProfile.BioThirdPerson?.Length > 4000)
                return new(false, "The third-person biography must be at most 4000 characters.");
            if (originalSpeaker is null)
            {
                speaker.BioThirdPerson = safeProfile.BioThirdPerson;
                speaker.Email = proof.Email;
                db.Speakers.Add(speaker);
            }
            else ApplyProfile(speaker, safeProfile);
            if (isPublic) speaker.Email = proof.Email;
            speaker.UpdatedAtUtc = DateTime.UtcNow;
            var session = new EventSpeakerSession
            {
                MeetupEventId = evt.Id, SpeakerId = speaker.Id, Speaker = speaker,
                TalkTitle = talkTitle.Trim(), TalkDescription = talkDescription,
                OrderIndex = evt.Sessions.Select(s => s.OrderIndex).DefaultIfEmpty(0).Max() + 1
            };
            evt.Sessions.Add(session);
            db.EventSpeakerSessions.Add(session);
            var title = EventTitles.FromSessions(evt.Sessions);
            if (title is not null && shouldGenerateTitle)
                evt.Title = title[..Math.Min(title.Length, 250)];
            evt.UpdatedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(evt.ShortBitlyUrl))
                evt.ShortBitlyUrl = ShortUrl.ForEvent(evt.MeetupGroup!.ShortUrlStem, evt.EventDateTimeUtc);
            proof.UsedAtUtc = DateTime.UtcNow;
            proof.BookedSessionId = session.Id;
            proof.BookedSession = session;
            await db.SaveChangesAsync(cancellationToken);
            return new(true, "Your speaking booking is confirmed.", evt, speaker);
        }, cancellationToken);
    }

    private async Task<T> InTransactionAsync<T>(Func<AppDbContext, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            var result = await action(db);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private static async Task<SpeakerBookingResult> PreviousBookingAsync(AppDbContext db, SpeakerBookingToken proof, CancellationToken cancellationToken)
    {
        var previous = await db.EventSpeakerSessions.AsNoTracking().Include(s => s.Speaker)
            .Include(s => s.MeetupEvent).ThenInclude(e => e!.MeetupGroup)
            .SingleOrDefaultAsync(s => s.Id == proof.BookedSessionId, cancellationToken);
        return previous is null ? new(false, InvalidLink) :
            new(true, "Your booking is already confirmed.", previous.MeetupEvent, previous.Speaker, true);
    }

    private async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var proof = await db.SpeakerBookingTokens.SingleAsync(t => t.Id == id, cancellationToken);
        proof.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<SpeakerBookingToken?> LockProofAsync(AppDbContext db, string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token ?? string.Empty);
        // Lock group before proof/event consistently, also serializing key rotation and disable.
        var groupId = await db.SpeakerBookingTokens.AsNoTracking().Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.MeetupGroupId).SingleOrDefaultAsync(cancellationToken);
        if (groupId is null) return null;
        await LockGroupAsync(db, groupId.Value, cancellationToken);
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [SpeakerBookingTokens] WITH (UPDLOCK, HOLDLOCK) WHERE [TokenHash] = {hash}", cancellationToken);
        return await FindProofAsync(db, token ?? string.Empty, cancellationToken);
    }

    private static async Task LockGroupAsync(AppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [MeetupGroups] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}", cancellationToken);
    }

    private static Task<SpeakerBookingToken?> FindProofAsync(AppDbContext db, string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        return db.SpeakerBookingTokens.Include(t => t.MeetupGroup).Include(t => t.Speaker)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    private static async Task<Speaker?> FindVerifiedSpeakerAsync(AppDbContext db, string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var matches = await db.Speakers.Where(s => s.Email != null && s.Email.Trim().ToLower() == normalized)
            .Take(2).ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool Valid(SpeakerBookingToken? proof, bool allowUsed = false) =>
        proof is not null && proof.RevokedAtUtc is null &&
        (proof.ExpiresAtUtc > DateTime.UtcNow || (allowUsed && proof.UsedAtUtc is not null)) &&
        (proof.SpeakerId is not null || (proof.MeetupGroup!.CfpEnabled && proof.PublicKey == proof.MeetupGroup.CfpKey));

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static Speaker CopyProfile(Speaker profile)
    {
        var speaker = new Speaker();
        ApplyProfile(speaker, profile);
        return speaker;
    }

    private static void PrepareBio(Speaker profile, Speaker? original)
    {
        var changed = original is null || original.Bio != profile.Bio ||
            original.Name != profile.Name || original.Pronouns != profile.Pronouns;
        profile.BioThirdPerson = changed ? null : original!.BioThirdPerson;
        SpeakerBio.EnsureThirdPerson(profile, force: changed);
    }

    private static void ApplyProfile(Speaker target, Speaker source)
    {
        target.Name = source.Name?.Trim() ?? string.Empty;
        target.Email = source.Email?.Trim();
        target.Bio = source.Bio;
        target.BioThirdPerson = source.BioThirdPerson;
        target.Pronouns = source.Pronouns;
        target.PhotoBlobUrl = source.PhotoBlobUrl;
        target.LinkedInUrl = source.LinkedInUrl;
        target.TwitterHandle = source.TwitterHandle;
        target.BlueSkyHandle = source.BlueSkyHandle;
        target.GitHubHandle = source.GitHubHandle;
        target.FacebookUrl = source.FacebookUrl;
        target.WebsiteUrl = source.WebsiteUrl;
    }
}
