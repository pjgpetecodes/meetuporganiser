using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MeetupOrganiser.Tests;

public class SpeakerBookingSqlTests
{
    [BookingSqlFact]
    public async Task StaleOrganizerReschedule_RequiresExplicitAcknowledgementAfterConcurrentBooking()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        var date = await f.AddDateAsync();
        await using var stale = f.Factory.CreateDbContext();
        var staleDate = await stale.MeetupEvents.Include(e => e.Sessions).SingleAsync(e => e.Id == date.Id);
        Assert.Empty(staleDate.Sessions);
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        Assert.True((await f.Service.BookAsync(token, date.Id, f.Profile(), "Talk", null)).Success);
        var rescheduled = date.EventDateTimeUtc.AddDays(7);
        staleDate.EventDateTimeUtc = rescheduled;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveChangesAsync());
        Assert.Contains("Reload the event and acknowledge", failure.Message);
        await using (var check = f.Factory.CreateDbContext())
            Assert.Equal(date.EventDateTimeUtc, (await check.MeetupEvents.SingleAsync()).EventDateTimeUtc);
        stale.AllowBookedDateChange = true;
        await stale.SaveChangesAsync();
        await using var final = f.Factory.CreateDbContext();
        Assert.Equal(rescheduled, (await final.MeetupEvents.SingleAsync()).EventDateTimeUtc);
        Assert.Single(await final.EventSpeakerSessions.ToListAsync());
    }

    [BookingSqlFact]
    public async Task DeletingBookedSessionAndEvent_NullsBookingReferenceButKeepsProofConsumed()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        var firstDate = await f.AddDateAsync();
        var secondDate = await f.AddDateAsync();
        var firstToken = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var secondToken = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        Assert.True((await f.Service.BookAsync(firstToken, firstDate.Id, f.Profile(), "Talk", null)).Success);
        Assert.True((await f.Service.BookAsync(secondToken, secondDate.Id, f.Profile(), "Talk", null)).Success);
        await using (var db = f.Factory.CreateDbContext())
        {
            var session = await db.EventSpeakerSessions.SingleAsync(s => s.MeetupEventId == firstDate.Id);
            db.Remove(session);
            await db.SaveChangesAsync();
        }
        await using (var db = f.Factory.CreateDbContext())
        {
            // Do not load sessions or tokens: exercise SQL Server cascade and SET NULL.
            db.Remove(await db.MeetupEvents.SingleAsync(e => e.Id == secondDate.Id));
            await db.SaveChangesAsync();
        }
        await using var check = f.Factory.CreateDbContext();
        Assert.Empty(await check.EventSpeakerSessions.ToListAsync());
        var proofs = await check.SpeakerBookingTokens.ToListAsync();
        Assert.Equal(2, proofs.Count);
        Assert.All(proofs, proof =>
        {
            Assert.Null(proof.BookedSessionId);
            Assert.NotNull(proof.UsedAtUtc);
        });
        Assert.Null(await f.Service.GetInvitationAsync(firstToken));
        Assert.Null(await f.Service.GetInvitationAsync(secondToken));
        Assert.False((await f.Service.BookAsync(firstToken, firstDate.Id, f.Profile(), "Retry", null)).Success);
    }

    [BookingSqlFact]
    public async Task ConcurrentPublicProofsForSameEmail_CannotOccupyBothPlaces()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        var date = await f.AddDateAsync();
        var first = await f.RequestAsync();
        var second = await f.RequestAsync("DIRECTORY@example.com");
        Assert.True((await f.Service.VerifyEmailAsync(first)).Success);
        Assert.True((await f.Service.VerifyEmailAsync(second)).Success);
        var outcomes = await Task.WhenAll(
            f.Service.BookAsync(first, date.Id, f.Profile(), "First talk", null),
            f.Service.BookAsync(second, date.Id, f.Profile(), "Second talk", null));
        Assert.Single(outcomes, outcome => outcome.Success);
        await using var check = f.Factory.CreateDbContext();
        Assert.Single(await check.EventSpeakerSessions.ToListAsync());
        Assert.Single(await check.Speakers.ToListAsync());
    }

    [BookingSqlFact]
    public async Task ConcurrentPublicClaims_FinalPlaceOnlyCommitsOnce()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        var date = await f.AddDateAsync();
        await using (var db = f.Factory.CreateDbContext())
        {
            db.Add(f.Session(date));
            await db.SaveChangesAsync();
        }
        var first = await f.RequestAsync("first@example.com");
        var second = await f.RequestAsync("second@example.com");
        Assert.True((await f.Service.VerifyEmailAsync(first)).Success);
        Assert.True((await f.Service.VerifyEmailAsync(second)).Success);
        var outcomes = await Task.WhenAll(
            f.Service.BookAsync(first, date.Id, new Speaker { Name = "First", Email = "first@example.com" }, "Talk", null),
            f.Service.BookAsync(second, date.Id, new Speaker { Name = "Second", Email = "second@example.com" }, "Talk", null));
        Assert.Single(outcomes, o => o.Success);
        await using var check = f.Factory.CreateDbContext();
        Assert.Equal(2, await check.EventSpeakerSessions.CountAsync());
        Assert.Single(await check.SpeakerBookingTokens.Where(t => t.UsedAtUtc != null).ToListAsync());
        Assert.Single(await check.SpeakerBookingTokens.Where(t => t.UsedAtUtc == null).ToListAsync());
    }

    [BookingSqlFact]
    public async Task OrganizerSave_AndPublicBookingShareTheSameCapacityLock()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        var date = await f.AddDateAsync();
        await using (var db = f.Factory.CreateDbContext())
        {
            db.Add(f.Session(date));
            await db.SaveChangesAsync();
        }
        var token = await f.RequestAsync();
        Assert.True((await f.Service.VerifyEmailAsync(token)).Success);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> OrganizerAsync()
        {
            await using var db = f.Factory.CreateDbContext();
            db.Add(f.Session(date));
            await start.Task;
            try
            {
                await db.SaveChangesAsync();
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("two places"))
            {
                return false;
            }
        }
        async Task<bool> ApplicantAsync()
        {
            await start.Task;
            return (await f.Service.BookAsync(token, date.Id, f.Profile(), "Public talk", null)).Success;
        }
        var organizer = OrganizerAsync();
        var applicant = ApplicantAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(organizer, applicant);
        Assert.Single(outcomes, success => success);
        await using var check = f.Factory.CreateDbContext();
        Assert.Equal(2, await check.EventSpeakerSessions.CountAsync());
    }

    [BookingSqlFact]
    public async Task ConcurrentSameToken_DifferentDatesIsIdempotentAndCannotDoubleClaim()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        var first = await f.AddDateAsync();
        var second = await f.AddDateAsync();
        var token = await f.Service.CreateInvitationAsync(f.Group.Id, f.Speaker.Id);
        var outcomes = await Task.WhenAll(
            f.Service.BookAsync(token, first.Id, f.Profile(), "First talk", null),
            f.Service.BookAsync(token, second.Id, f.Profile(), "Second talk", null));
        Assert.All(outcomes, outcome => Assert.True(outcome.Success, outcome.Message));
        Assert.Single(outcomes, o => o.AlreadyBooked);
        Assert.Equal(outcomes[0].Event!.Id, outcomes[1].Event!.Id);
        await using var check = f.Factory.CreateDbContext();
        Assert.Single(await check.EventSpeakerSessions.ToListAsync());
    }

    [BookingSqlFact]
    public async Task ConcurrentVerificationRequests_CannotExceedEmailBudget()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var f = await BookingFixture.CreateAsync(database.Options);
        for (var i = 0; i < 4; i++) await f.RequestAsync();
        var outcomes = await Task.WhenAll(
            f.Service.RequestVerificationAsync(f.Group.CfpKey!, "DIRECTORY@example.com", "https://example.com"),
            f.Service.RequestVerificationAsync(f.Group.CfpKey!, "directory@example.com", "https://example.com"));
        Assert.Single(outcomes, o => o.Success);
        await using var check = f.Factory.CreateDbContext();
        Assert.Equal(5, await check.SpeakerBookingTokens.CountAsync());
    }

    [BookingSqlFact]
    public async Task AdditiveMigration_PreservesExistingEventsSessionsAndFixedTokens()
    {
        await using var database = await BookingSqlDatabase.CreateAsync(migrate: false);
        await using var db = new AppDbContext(database.Options);
        var migrations = db.Database.GetMigrations().ToArray();
        await db.GetService<IMigrator>().MigrateAsync(migrations.Single(m => m.EndsWith("_AddMeetupAttendeeCount")));
        var group = Guid.NewGuid();
        var speaker = Guid.NewGuid();
        var date = Guid.NewGuid();
        var session = Guid.NewGuid();
        var fixedToken = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO MeetupGroups (Id, Name, PrimaryColorHex, SecondaryColorHex, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({group}, N'Existing group', N'#000000', N'#ffffff', SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO Speakers (Id, Name, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({speaker}, N'Existing speaker', SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO MeetupEvents (Id, MeetupGroupId, Title, EventDateTimeUtc, DurationMinutes, Status, Format, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({date}, {group}, N'Existing event', DATEADD(day, 30, SYSUTCDATETIME()), 90, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO EventSpeakerSessions (Id, MeetupEventId, SpeakerId, TalkTitle, OrderIndex)
            VALUES ({session}, {date}, {speaker}, N'', 1);
            INSERT INTO MagicLinkTokens (Id, TokenHash, SpeakerId, EventSpeakerSessionId, ExpiresAtUtc, IsRevoked, CreatedAtUtc)
            VALUES ({fixedToken}, N'fixed-token-hash', {speaker}, {session}, DATEADD(day, 14, SYSUTCDATETIME()), 0, SYSUTCDATETIME());
            """);
        await db.Database.MigrateAsync();
        Assert.False((await db.MeetupGroups.SingleAsync()).CfpEnabled);
        Assert.False((await db.MeetupEvents.SingleAsync()).IsOpenForBookings);
        Assert.Equal(session, (await db.EventSpeakerSessions.SingleAsync()).Id);
        var preserved = await db.MagicLinkTokens.SingleAsync();
        Assert.Equal(fixedToken, preserved.Id);
        Assert.True(preserved.IsValid);
        Assert.Empty(await db.SpeakerBookingTokens.ToListAsync());
    }
}

public sealed class BookingSqlFactAttribute : FactAttribute
{
    public BookingSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEETUP_BOOKING_TEST_SQL_SERVER")))
            Skip = "Set MEETUP_BOOKING_TEST_SQL_SERVER to a SQL Server connection with CREATE DATABASE permission. Tests create and delete only unique MeetupBookingTests_* databases.";
    }
}

internal sealed class BookingSqlDatabase : IAsyncDisposable
{
    public DbContextOptions<AppDbContext> Options { get; }

    private BookingSqlDatabase()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MEETUP_BOOKING_TEST_SQL_SERVER")!)
        {
            InitialCatalog = $"MeetupBookingTests_{Guid.NewGuid():N}",
            TrustServerCertificate = true
        };
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.EnableRetryOnFailure()).Options;
    }

    public static async Task<BookingSqlDatabase> CreateAsync(bool migrate = true)
    {
        var database = new BookingSqlDatabase();
        try
        {
            if (migrate)
            {
                await using var db = new AppDbContext(database.Options);
                await db.Database.MigrateAsync();
            }
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = new AppDbContext(Options);
        await db.Database.EnsureDeletedAsync();
    }
}
