using System.Security.Cryptography;
using System.Text;
using MeetupOrganiser.Web;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MeetupOrganiser.Tests;

public class ExtensionAccessTests
{
    internal static string Verifier() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static string Challenge(string verifier) => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Pairing_RequiresApprovalAndVerifierAndCanOnlyBeRedeemedOnce()
    {
        var fixture = await BookingFixture.CreateAsync();
        var access = new ExtensionAccess(TimeProvider.System, fixture.Factory);
        var verifier = Verifier();
        var id = access.Start(Challenge(verifier))!;
        Assert.Equal("pending", (await access.RedeemAsync(id, verifier)).Status);
        Assert.Equal("invalid", (await access.RedeemAsync(id, Verifier())).Status);
        Assert.True(access.Approve(id, "organiser"));
        Assert.False(access.Approve(id, "someone-else"));
        Assert.Equal("invalid", (await access.RedeemAsync(id, Verifier())).Status);
        var grant = await access.RedeemAsync(id, verifier);
        Assert.Equal("approved", grant.Status);
        Assert.Equal("organiser", await access.OwnerAsync(grant.Token!));
        Assert.Equal("invalid", (await access.RedeemAsync(id, verifier)).Status);
        Assert.Null(await access.OwnerAsync(Verifier()));
        await access.RevokeTokenAsync(grant.Token!);
        Assert.Null(await access.OwnerAsync(grant.Token!));
    }

    [Fact]
    public async Task ExpiryAndRevocation_RemovePendingRequestsAndGrants()
    {
        var clock = new Clock();
        var fixture = await BookingFixture.CreateAsync();
        var access = new ExtensionAccess(clock, fixture.Factory);
        var verifier = Verifier();
        var id = access.Start(Challenge(verifier))!;
        clock.Now = clock.Now.AddMinutes(6);
        Assert.False(access.Approve(id, "organiser"));
        Assert.Equal("invalid", (await access.RedeemAsync(id, verifier)).Status);
        id = access.Start(Challenge(verifier))!;
        Assert.True(access.Approve(id, "organiser"));
        var token = (await access.RedeemAsync(id, verifier)).Token!;
        clock.Now = clock.Now.AddDays(29);
        Assert.Equal("organiser", await access.OwnerAsync(token));
        clock.Now = clock.Now.AddDays(1);
        Assert.Null(await access.OwnerAsync(token));
        var pending = access.Start(Challenge(verifier))!;
        access.Approve(pending, "organiser");
        var another = access.Start(Challenge(verifier))!;
        access.Approve(another, "someone-else");
        var otherToken = (await access.RedeemAsync(another, verifier)).Token!;
        await access.RevokeAsync("organiser");
        Assert.Equal("invalid", (await access.RedeemAsync(pending, verifier)).Status);
        Assert.Equal("someone-else", await access.OwnerAsync(otherToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-challenge")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidRequests_AreRejected(string challenge)
    {
        var fixture = await BookingFixture.CreateAsync();
        var access = new ExtensionAccess(TimeProvider.System, fixture.Factory);
        Assert.Null(access.Start(challenge));
        Assert.Null(await access.OwnerAsync(challenge));
    }

    [Fact]
    public async Task Grants_SurviveNewServiceInstanceStoreOnlyHashAndRevokeAcrossInstances()
    {
        var fixture = await BookingFixture.CreateAsync();
        var clock = new Clock();
        var access = new ExtensionAccess(clock, fixture.Factory);
        var verifier = Verifier();
        var id = access.Start(Challenge(verifier))!;
        access.Approve(id, "organiser");
        var grant = await access.RedeemAsync(id, verifier);
        Assert.Equal(clock.Now.AddDays(30), grant.ExpiresAt);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var stored = Assert.Single(db.ExtensionGrants);
            Assert.NotEqual(grant.Token, stored.TokenHash);
            Assert.Equal(64, stored.TokenHash.Length);
        }
        var restarted = new ExtensionAccess(clock, fixture.Factory);
        Assert.Equal("organiser", await restarted.OwnerAsync(grant.Token!));
        await restarted.RevokeAsync("organiser");
        Assert.Null(await access.OwnerAsync(grant.Token!));
    }

    [BookingSqlFact]
    public async Task DurableGrantMigration_PreservesEventsAndPersistsRevocableGrantsInSql()
    {
        await using var database = await BookingSqlDatabase.CreateAsync(migrate: false);
        await using (var db = new AppDbContext(database.Options))
            await db.GetService<IMigrator>().MigrateAsync("20261019104234_AddSpeakerBookings");
        var fixture = await BookingFixture.CreateAsync(database.Options);
        var meetup = await fixture.AddDateAsync();
        await using (var db = new AppDbContext(database.Options))
        {
            await db.Database.MigrateAsync();
            Assert.Equal(meetup.Id, (await db.MeetupEvents.SingleAsync()).Id);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        var clock = new Clock();
        var access = new ExtensionAccess(clock, fixture.Factory);
        var verifier = Verifier();
        var request = access.Start(Challenge(verifier))!;
        Assert.True(access.Approve(request, "sql-organiser"));
        var grant = await access.RedeemAsync(request, verifier);
        var restarted = new ExtensionAccess(clock, fixture.Factory);
        Assert.Equal("sql-organiser", await restarted.OwnerAsync(grant.Token!));
        clock.Now = clock.Now.AddDays(30);
        Assert.Null(await restarted.OwnerAsync(grant.Token!));
        clock.Now = clock.Now.AddDays(-1);
        await restarted.RevokeTokenAsync(grant.Token!);
        Assert.Null(await access.OwnerAsync(grant.Token!));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
