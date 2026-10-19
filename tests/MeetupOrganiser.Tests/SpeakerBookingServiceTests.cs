using System.Text.RegularExpressions;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Tests;

public class SpeakerBookingServiceTests
{
    [Fact]
    public async Task Availability_IsGroupScopedAndCountsBlankTalksAndPlaceholders()
    {
        var f = await BookingFixture.CreateAsync();
        var empty = f.Date();
        var shared = f.Date();
        var full = f.Date();
        var closed = f.Date(); closed.IsOpenForBookings = false;
        var past = f.Date(); past.EventDateTimeUtc = DateTime.UtcNow.AddDays(-1);
        var live = f.Date(); live.Status = EventStatus.Published;
        var cancelled = f.Date(); cancelled.Status = EventStatus.Cancelled;
        var other = f.Date(); other.MeetupGroup = new MeetupGroup { Name = "Other" }; other.MeetupGroupId = other.MeetupGroup.Id;
        await using (var db = f.Factory.CreateDbContext())
        {
            db.AddRange(empty, shared, full, closed, past, live, cancelled, other);
            db.AddRange(f.Session(shared), f.Session(full), f.Session(full));
            await db.SaveChangesAsync();
        }
        var dates = await f.Service.GetAvailableDatesAsync(f.Group.Id);
        Assert.Equal(2, dates.Count);
        Assert.Equal(2, dates.Single(d => d.Id == empty.Id).RemainingPlaces);
        Assert.Equal(1, dates.Single(d => d.Id == shared.Id).RemainingPlaces);
    }

    [Fact]
    public async Task PrivateInvitation_IsHashedDetachedAndCreatesNoReservation()
    {
        var f = await BookingFixture.CreateAsync();
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        await using var db = f.Factory.CreateDbContext();
        var proof = await db.SpeakerBookingTokens.SingleAsync();
        Assert.NotEqual(token, proof.TokenHash);
        Assert.InRange(proof.ExpiresAtUtc, DateTime.UtcNow.AddDays(13), DateTime.UtcNow.AddDays(15));
        Assert.Empty(await db.EventSpeakerSessions.ToListAsync());
        var invitation = await f.Service.GetInvitationAsync(token);
        Assert.NotNull(invitation);
        Assert.False(invitation.IsPublic);
        invitation.Speaker.Name = "Unsaved";
        Assert.Equal("Directory speaker", (await db.Speakers.SingleAsync()).Name);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("used")]
    [InlineData("unknown")]
    public async Task InvalidProofs_DoNotExposeInvitationOrBook(string state)
    {
        var f = await BookingFixture.CreateAsync();
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        await using (var db = f.Factory.CreateDbContext())
        {
            var proof = await db.SpeakerBookingTokens.SingleAsync();
            if (state == "expired") proof.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            if (state == "revoked") proof.RevokedAtUtc = DateTime.UtcNow;
            if (state == "used") proof.UsedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        if (state == "unknown") token = "not-a-token";
        Assert.Null(await f.Service.GetInvitationAsync(token));
        Assert.False((await f.Service.BookAsync(token, Guid.NewGuid(), f.Profile(), "Talk", null)).Success);
    }

    [Fact]
    public async Task PrivateBooking_OnlyUpdatesLinkedProfileAndIsIdempotent()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var profile = f.Profile();
        profile.Id = Guid.NewGuid();
        profile.CreatedAtUtc = DateTime.MinValue;
        profile.Sessions.Add(f.Session(date));
        profile.Name = new string('N', 200);
        profile.Bio = "I build helpful tools.";
        var first = await f.Service.BookAsync(token, date.Id, profile, new string('T', 300), "Abstract");
        var second = await f.Service.BookAsync(token, Guid.NewGuid(), profile, "Different", null);
        Assert.True(first.Success, first.Message);
        Assert.Equal(f.Speaker.Id, first.Speaker!.Id);
        Assert.Equal(f.Speaker.CreatedAtUtc, first.Speaker.CreatedAtUtc);
        Assert.NotNull(first.Speaker.BioThirdPerson);
        Assert.Equal(250, first.Event!.Title.Length);
        Assert.Equal(EventStatus.Draft, first.Event.Status);
        Assert.NotNull(first.Event.ShortBitlyUrl);
        Assert.True(second.AlreadyBooked);
        Assert.Equal(first.Event.Id, second.Event!.Id);
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.EventSpeakerSessions.ToListAsync());
        Assert.Single(await db.Speakers.ToListAsync());
        Assert.Empty(f.Email.Messages);
        var retryWithoutForm = await f.Service.BookAsync(token, date.Id, new Speaker(), "", null);
        Assert.True(retryWithoutForm.AlreadyBooked);
    }

    [Fact]
    public async Task PublicFlow_OnlyAfterVerificationPrefillsAndUpdatesSingleEmailMatch()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var token = await f.RequestAsync(" DIRECTORY@example.com ");
        var invitation = await f.Service.GetInvitationAsync(token);
        Assert.True(invitation!.RequiresEmailVerification);
        Assert.Empty(invitation.Speaker.Name);
        Assert.False(invitation.HasExistingProfile);
        Assert.Null(invitation.Speaker.Email);
        Assert.False((await f.Service.BookAsync(token, date.Id, f.Profile(), "Talk", null)).Success);
        await using (var db = f.Factory.CreateDbContext())
        {
            Assert.Null((await db.SpeakerBookingTokens.SingleAsync()).VerifiedAtUtc);
            Assert.Empty(await db.EventSpeakerSessions.ToListAsync());
            Assert.Single(await db.Speakers.ToListAsync());
        }
        Assert.True((await f.Service.VerifyEmailAsync(token)).Success);
        Assert.True((await f.Service.VerifyEmailAsync(token)).Success);
        invitation = await f.Service.GetInvitationAsync(token);
        Assert.False(invitation!.RequiresEmailVerification);
        Assert.Equal("directory@example.com", invitation.Speaker.Email);
        Assert.Equal("Directory speaker", invitation.Speaker.Name);
        Assert.True(invitation.HasExistingProfile);
        var profile = f.Profile();
        profile.Email = "different@example.com";
        Assert.False((await f.Service.BookAsync(token, date.Id, profile, "Talk", null)).Success);
        profile.Email = "directory@example.com";
        profile.Id = f.Speaker.Id;
        profile.Name = "Public applicant";
        profile.Bio = "I build community tools.";
        profile.BioThirdPerson = "Client-supplied biography must not be trusted.";
        var booked = await f.Service.BookAsync(token, date.Id, profile, "Talk", null);
        Assert.True(booked.Success, booked.Message);
        Assert.Equal(f.Speaker.Id, booked.Speaker!.Id);
        Assert.NotEqual(profile.BioThirdPerson, booked.Speaker.BioThirdPerson);
        Assert.Contains("Public", booked.Speaker.BioThirdPerson);
        await using var check = f.Factory.CreateDbContext();
        Assert.Equal("Public applicant", (await check.Speakers.FindAsync(f.Speaker.Id))!.Name);
        Assert.Equal(1, await check.Speakers.CountAsync());
        Assert.Single(f.Email.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicFlow_NoUniqueEmailMatchCreatesNewProfileAndIgnoresSubmittedSpeakerId(bool duplicate)
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        if (duplicate)
        {
            await using var db = f.Factory.CreateDbContext();
            db.Speakers.Add(new Speaker { Name = "Another match", Email = " DIRECTORY@EXAMPLE.COM " });
            await db.SaveChangesAsync();
        }
        var token = await f.RequestAsync(duplicate ? "directory@example.com" : "new@example.com");
        Assert.True((await f.Service.VerifyEmailAsync(token)).Success);
        var invitation = await f.Service.GetInvitationAsync(token);
        Assert.False(invitation!.HasExistingProfile);
        Assert.Empty(invitation.Speaker.Name);
        var profile = f.Profile();
        profile.Id = f.Speaker.Id;
        profile.Email = duplicate ? "directory@example.com" : "new@example.com";
        var booking = await f.Service.BookAsync(token, date.Id, profile, "New talk", null);
        Assert.True(booking.Success, booking.Message);
        Assert.NotEqual(f.Speaker.Id, booking.Speaker!.Id);
        await using var check = f.Factory.CreateDbContext();
        Assert.Equal("Directory speaker", (await check.Speakers.FindAsync(f.Speaker.Id))!.Name);
        Assert.Equal(duplicate ? 3 : 2, await check.Speakers.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicProof_IsRevokedByDisableOrRotationEvenAfterVerification(bool rotate)
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var token = await f.RequestAsync();
        Assert.True((await f.Service.VerifyEmailAsync(token)).Success);
        await using (var db = f.Factory.CreateDbContext())
        {
            var group = await db.MeetupGroups.SingleAsync();
            if (rotate) group.CfpKey = Guid.NewGuid().ToString("N");
            else group.CfpEnabled = false;
            await db.SaveChangesAsync();
        }
        Assert.Null(await f.Service.GetPublicGroupAsync(f.Group.CfpKey!));
        Assert.Null(await f.Service.GetInvitationAsync(token));
        Assert.False((await f.Service.VerifyEmailAsync(token)).Success);
        Assert.False((await f.Service.BookAsync(token, date.Id, f.Profile(), "Talk", null)).Success);
    }

    [Fact]
    public async Task PublicVerification_ExpiresWithoutReservingOrAllowingBooking()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var token = await f.RequestAsync();
        Assert.True((await f.Service.VerifyEmailAsync(token)).Success);
        await using (var db = f.Factory.CreateDbContext())
        {
            var proof = await db.SpeakerBookingTokens.SingleAsync();
            proof.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        Assert.Null(await f.Service.GetInvitationAsync(token));
        Assert.False((await f.Service.VerifyEmailAsync(token)).Success);
        Assert.False((await f.Service.BookAsync(token, date.Id, f.Profile(), "Talk", null)).Success);
        Assert.Equal(2, (await f.Service.GetAvailableDatesAsync(f.Group.Id)).Single().RemainingPlaces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateBooking_PreservesCuratedBioOnlyWhenSourceProfileIsUnchanged(bool changed)
    {
        var f = await BookingFixture.CreateAsync();
        await using (var db = f.Factory.CreateDbContext())
        {
            var speaker = await db.Speakers.SingleAsync();
            speaker.Bio = "I build tools.";
            speaker.BioThirdPerson = "A curated third-person biography.";
            speaker.Pronouns = "they/them";
            await db.SaveChangesAsync();
        }
        var date = await f.AddDateAsync();
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var invitation = await f.Service.GetInvitationAsync(token);
        var profile = invitation!.Speaker;
        profile.BioThirdPerson = "Untrusted client override.";
        if (changed)
        {
            profile.Name = "New name";
            profile.Bio = "I organize communities.";
        }
        var result = await f.Service.BookAsync(token, date.Id, profile, "Talk", null);
        Assert.True(result.Success, result.Message);
        if (changed)
        {
            Assert.NotEqual("A curated third-person biography.", result.Speaker!.BioThirdPerson);
            Assert.Contains("New", result.Speaker.BioThirdPerson);
        }
        else Assert.Equal("A curated third-person biography.", result.Speaker!.BioThirdPerson);
    }

    [Fact]
    public async Task SeparateVerifiedProofs_ForSameEmailCannotDoubleBookOneDate()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var first = await f.RequestAsync();
        var second = await f.RequestAsync("DIRECTORY@example.com");
        Assert.True((await f.Service.VerifyEmailAsync(first)).Success);
        Assert.True((await f.Service.VerifyEmailAsync(second)).Success);
        Assert.True((await f.Service.BookAsync(first, date.Id, f.Profile(), "Talk", null)).Success);
        Assert.False((await f.Service.BookAsync(second, date.Id, f.Profile(), "Another talk", null)).Success);
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.EventSpeakerSessions.ToListAsync());
        Assert.Single(await db.Speakers.ToListAsync());
        Assert.NotNull(await f.Service.GetInvitationAsync(second));
    }
    [Fact]
    public async Task Booking_PreservesManuallyAuthoredEventTitle()
    {
        var f = await BookingFixture.CreateAsync();
        var date = f.Date();
        date.Title = "Special community workshop";
        await using (var db = f.Factory.CreateDbContext())
        {
            db.Add(date);
            await db.SaveChangesAsync();
        }
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var result = await f.Service.BookAsync(token, date.Id, f.Profile(), "Talk title", null);
        Assert.True(result.Success, result.Message);
        Assert.Equal(date.Title, result.Event!.Title);
    }

    [Fact]
    public async Task SecondBooking_ExtendsPreviouslyGeneratedEventTitle()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var first = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var firstResult = await f.Service.BookAsync(first, date.Id, f.Profile(), "First talk", null);
        Assert.True(firstResult.Success, firstResult.Message);
        Assert.Equal("First talk - Submitted name", firstResult.Event!.Title);
        var second = await f.RequestAsync("second@example.com");
        Assert.True((await f.Service.VerifyEmailAsync(second)).Success);
        var secondResult = await f.Service.BookAsync(second, date.Id,
            new Speaker { Name = "Second speaker", Email = "second@example.com" }, "Second talk", null);
        Assert.True(secondResult.Success, secondResult.Message);
        Assert.Equal("First talk - Submitted name | Second talk - Second speaker", secondResult.Event!.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeliveryFailure_IsExplicitRevokesProofAndAllowsRetry(bool throws)
    {
        var f = await BookingFixture.CreateAsync();
        f.Email.Deliver = false;
        f.Email.Throw = throws;
        var result = await f.Service.RequestVerificationAsync(f.Group.CfpKey!, "directory@example.com", "https://example.com");
        Assert.False(result.Success);
        await using var db = f.Factory.CreateDbContext();
        Assert.NotNull((await db.SpeakerBookingTokens.SingleAsync()).RevokedAtUtc);
        Assert.Empty(await db.EventSpeakerSessions.ToListAsync());
        f.Email.Deliver = true;
        f.Email.Throw = false;
        Assert.True((await f.Service.RequestVerificationAsync(f.Group.CfpKey!, "directory@example.com", "https://example.com")).Success);
    }

    [Fact]
    public async Task VerificationLink_IncludesPreferredDateWithoutReservingPlace()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var result = await f.Service.RequestVerificationAsync(f.Group.CfpKey!, "directory@example.com", "https://example.com", date.Id);
        Assert.True(result.Success);
        Assert.Contains($"?dateId={date.Id}", Assert.Single(f.Email.Messages));
        Assert.Equal($"{f.Group.Name}: complete your speaker profile and submit your talk", Assert.Single(f.Email.Subjects));
        Assert.Contains("Opening this personal link confirms your email address", f.Email.Messages[0]);
        Assert.Contains("Your preferred date is included", f.Email.Messages[0]);
        Assert.Contains("30 minutes", f.Email.Messages[0]);
        await using var db = f.Factory.CreateDbContext();
        Assert.Empty(await db.EventSpeakerSessions.ToListAsync());
    }

    [Fact]
    public async Task ResendLimits_ArePersistedPerNormalizedEmailAndPerGroup()
    {
        var f = await BookingFixture.CreateAsync();
        for (var i = 0; i < 5; i++) await f.RequestAsync(" DIRECTORY@example.com ");
        Assert.False((await f.Service.RequestVerificationAsync(f.Group.CfpKey!, "directory@EXAMPLE.com", "https://example.com")).Success);
        await using (var db = f.Factory.CreateDbContext())
        {
            for (var i = 0; i < 95; i++)
                db.SpeakerBookingTokens.Add(new SpeakerBookingToken
                {
                    MeetupGroupId = f.Group.Id,
                    Email = $"person{i}@example.com",
                    PublicKey = f.Group.CfpKey,
                    TokenHash = Guid.NewGuid().ToString("N"),
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
                });
            await db.SaveChangesAsync();
        }
        Assert.False((await f.Service.RequestVerificationAsync(f.Group.CfpKey!, "new@example.com", "https://example.com")).Success);
        Assert.Equal(5, f.Email.Messages.Count);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("closed")]
    [InlineData("past")]
    [InlineData("live")]
    [InlineData("group")]
    [InlineData("duplicate")]
    public async Task BookingRechecksEligibility_WithoutConsumingProof(string reason)
    {
        var f = await BookingFixture.CreateAsync();
        var date = f.Date();
        if (reason == "closed") date.IsOpenForBookings = false;
        if (reason == "past") date.EventDateTimeUtc = DateTime.UtcNow.AddDays(-1);
        if (reason == "live") date.Status = EventStatus.Published;
        if (reason == "group")
        {
            date.MeetupGroup = new MeetupGroup { Name = "Different" };
            date.MeetupGroupId = date.MeetupGroup.Id;
        }
        await using (var db = f.Factory.CreateDbContext())
        {
            db.Add(date);
            if (reason == "full") db.AddRange(f.Session(date), f.Session(date));
            if (reason == "duplicate") db.Add(new EventSpeakerSession { MeetupEventId = date.Id, SpeakerId = f.Speaker.Id });
            await db.SaveChangesAsync();
        }
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        Assert.False((await f.Service.BookAsync(token, date.Id, f.Profile(), "Talk", null)).Success);
        Assert.NotNull(await f.Service.GetInvitationAsync(token));
        var alternative = await f.AddDateAsync();
        Assert.True((await f.Service.BookAsync(token, alternative.Id, f.Profile(), "Talk", null)).Success);
    }

    [Fact]
    public async Task Validation_RejectsOversizedProfilesAndTalksWithoutWrites()
    {
        var f = await BookingFixture.CreateAsync();
        var date = await f.AddDateAsync();
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var profile = f.Profile();
        profile.Name = new string('N', 201);
        Assert.False((await f.Service.BookAsync(token, date.Id, profile, "Talk", null)).Success);
        profile.Name = "Valid";
        Assert.False((await f.Service.BookAsync(token, date.Id, profile, new string('T', 301), null)).Success);
        Assert.False((await f.Service.BookAsync(token, date.Id, profile, "Talk", new string('A', 6001))).Success);
        profile.Email = "not email";
        Assert.False((await f.Service.BookAsync(token, date.Id, profile, "Talk", null)).Success);
        await using var db = f.Factory.CreateDbContext();
        Assert.Empty(await db.EventSpeakerSessions.ToListAsync());
        Assert.Null((await db.SpeakerBookingTokens.SingleAsync()).UsedAtUtc);
    }

    [Fact]
    public async Task CentralGuard_ProtectsOrganizerAddsMovesOpeningAndDuplicatesButNotHistoricalDates()
    {
        var f = await BookingFixture.CreateAsync();
        var full = f.Date();
        var historical = f.Date(); historical.IsOpenForBookings = false;
        await using (var db = f.Factory.CreateDbContext())
        {
            db.AddRange(full, historical);
            db.AddRange(f.Session(full), f.Session(full), f.Session(historical), f.Session(historical), f.Session(historical));
            await db.SaveChangesAsync();
        }
        await using (var db = f.Factory.CreateDbContext())
        {
            db.Add(f.Session(full));
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }
        await using (var db = f.Factory.CreateDbContext())
        {
            var session = await db.EventSpeakerSessions.FirstAsync(s => s.MeetupEventId == historical.Id);
            session.MeetupEventId = full.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = f.Factory.CreateDbContext())
        {
            var date = await db.MeetupEvents.SingleAsync(e => e.Id == historical.Id);
            date.IsOpenForBookings = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = f.Factory.CreateDbContext())
        {
            var date = await db.MeetupEvents.SingleAsync(e => e.Id == historical.Id);
            date.Title = "Historical update remains supported";
            await db.SaveChangesAsync();
        }
        var empty = await f.AddDateAsync();
        await using (var db = f.Factory.CreateDbContext())
        {
            db.AddRange(new EventSpeakerSession { MeetupEventId = empty.Id, SpeakerId = f.Speaker.Id },
                new EventSpeakerSession { MeetupEventId = empty.Id, SpeakerId = f.Speaker.Id });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

    }

    [Fact]
    public async Task CentralGuard_UsesStoredBookingFlagWhenAnOrganizerHasAStaleClosedEvent()
    {
        var f = await BookingFixture.CreateAsync();
        var date = f.Date();
        date.IsOpenForBookings = false;
        await using (var seed = f.Factory.CreateDbContext())
        {
            seed.Add(date);
            await seed.SaveChangesAsync();
        }
        await using var stale = f.Factory.CreateDbContext();
        var staleDate = await stale.MeetupEvents.SingleAsync();
        await using (var current = f.Factory.CreateDbContext())
        {
            var currentDate = await current.MeetupEvents.SingleAsync();
            currentDate.IsOpenForBookings = true;
            await current.SaveChangesAsync();
        }
        staleDate.Title = "An unrelated title edit";
        stale.AddRange(f.Session(date), f.Session(date), f.Session(date));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveChangesAsync());
    }
}

internal sealed class BookingFixture
{
    public BookingContextFactory Factory { get; }
    public RecordingBookingEmail Email { get; } = new();
    public SpeakerBookingService Service { get; }
    public MeetupGroup Group { get; } = new() { Name = "Test group", CfpEnabled = true, CfpKey = Guid.NewGuid().ToString("N"), ShortUrlStem = "https://bit.ly/test" };
    public Speaker Speaker { get; } = new() { Name = "Directory speaker", Email = "directory@example.com" };

    private BookingFixture(DbContextOptions<AppDbContext> options)
    {
        Factory = new(options);
        Service = new(Factory, Email);
    }

    public static async Task<BookingFixture> CreateAsync(DbContextOptions<AppDbContext>? options = null)
    {
        var fixture = new BookingFixture(options ?? new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = fixture.Factory.CreateDbContext();
        db.AddRange(fixture.Group, fixture.Speaker);
        await db.SaveChangesAsync();
        return fixture;
    }

    public MeetupEvent Date() => new() { MeetupGroupId = Group.Id, Title = "Upcoming (speaker TBC)", IsOpenForBookings = true, EventDateTimeUtc = DateTime.UtcNow.AddDays(30) };
    public EventSpeakerSession Session(MeetupEvent date) => new()
    {
        MeetupEventId = date.Id,
        Speaker = new Speaker { Name = Speaker.PlaceholderName },
        TalkTitle = ""
    };
    public Speaker Profile() => new() { Name = "Submitted name", Email = Speaker.Email };
    public async Task<MeetupEvent> AddDateAsync()
    {
        var date = Date();
        await using var db = Factory.CreateDbContext();
        db.Add(date);
        await db.SaveChangesAsync();
        return date;
    }
    public async Task<string> RequestAsync(string email = "directory@example.com")
    {
        var result = await Service.RequestVerificationAsync(Group.CfpKey!, email, "https://example.com");
        Assert.True(result.Success, result.Message);
        return Regex.Match(Email.Messages.Last(), @"/speaker/choose/([a-f0-9]{64})").Groups[1].Value;
    }
}

internal sealed class BookingContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}

internal sealed class RecordingBookingEmail : IEmailService
{
    public List<string> Messages { get; } = [];
    public List<string> Subjects { get; } = [];
    public bool Deliver { get; set; } = true;
    public bool Throw { get; set; }
    public Task<bool> SendAsync(string to, string subject, string htmlBody, string? cc = null,
        IReadOnlyList<EmailAttachment>? attachments = null, CalendarInvite? invite = null, CancellationToken cancellationToken = default)
    {
        if (Throw) throw new InvalidOperationException("Delivery failed");
        Messages.Add(htmlBody);
        Subjects.Add(subject);
        return Task.FromResult(Deliver);
    }
}
