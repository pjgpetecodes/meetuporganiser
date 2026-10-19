using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Tests;

public class UpcomingMeetupSchedulerTests
{
    [Fact]
    public async Task CreatesWholeSelectionAsOpenDrafts_WithSharedDetailsAndUkTimes()
    {
        var factory = new CalendarDbFactory();
        var group = new MeetupGroup { Name = "Example", ShortUrlStem = "https://bit.ly/example" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(group);
            db.Add(new MeetupEvent { MeetupGroup = new MeetupGroup { Name = "Other group" }, EventDateTimeUtc = new(2032, 1, 15, 18, 30, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }
        var scheduler = new UpcomingMeetupScheduler(factory);
        var count = await scheduler.CreateDraftsAsync(group.Id,
            [new(2032, 7, 15), new(2032, 1, 15), new(2032, 7, 15)],
            new(18, 30), 120, EventFormat.InPerson, " Town Hall ", " Main Room ");

        Assert.Equal(2, count);
        await using var check = factory.CreateDbContext();
        var drafts = await check.MeetupEvents.Where(e => e.MeetupGroupId == group.Id).OrderBy(e => e.EventDateTimeUtc).ToListAsync();
        Assert.Equal(new DateTime(2032, 1, 15, 18, 30, 0, DateTimeKind.Utc), drafts[0].EventDateTimeUtc);
        Assert.Equal(new DateTime(2032, 7, 15, 17, 30, 0, DateTimeKind.Utc), drafts[1].EventDateTimeUtc);
        Assert.All(drafts, draft =>
        {
            Assert.Equal(EventStatus.Draft, draft.Status);
            Assert.True(draft.IsOpenForBookings);
            Assert.Empty(draft.Sessions);
            Assert.Equal(120, draft.DurationMinutes);
            Assert.Equal(EventFormat.InPerson, draft.Format);
            Assert.Equal("Town Hall", draft.LocationOrPlatform);
            Assert.Equal("Main Room", draft.VenueName);
            Assert.True(EventTitles.IsPlaceholder(draft.Title));
            Assert.Equal(ShortUrl.ForEvent(group.ShortUrlStem, draft.EventDateTimeUtc), draft.ShortBitlyUrl);
        });
    }

    [Fact]
    public async Task ExistingMeetupOnUkDay_BlocksWholeBatchEvenAtDifferentTime()
    {
        var factory = new CalendarDbFactory();
        var group = new MeetupGroup { Name = "Example" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(new MeetupEvent
            {
                MeetupGroup = group, Status = EventStatus.Cancelled,
                EventDateTimeUtc = new(2032, 7, 14, 23, 30, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new UpcomingMeetupScheduler(factory)
            .CreateDraftsAsync(group.Id, [new(2032, 7, 15), new(2032, 7, 16)], new(18, 30), 90, EventFormat.Hybrid, null, null));
        Assert.Contains("15 Jul 2032", error.Message);
        await using var check = factory.CreateDbContext();
        Assert.Single(await check.MeetupEvents.ToListAsync());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task ClockChangeTimes_RejectWholeBatch(int month)
    {
        var factory = new CalendarDbFactory();
        var lastDay = new DateOnly(2032, month, 31);
        var sunday = lastDay.AddDays(-(int)lastDay.DayOfWeek);
        await Assert.ThrowsAsync<ArgumentException>(() => new UpcomingMeetupScheduler(factory)
            .CreateDraftsAsync(Guid.NewGuid(), [new(2032, 1, 15), sunday], new(1, 30), 90, EventFormat.Hybrid, null, null));
        await using var check = factory.CreateDbContext();
        Assert.Empty(await check.MeetupEvents.ToListAsync());
    }

    [Fact]
    public async Task PastDatesAndEmptySelections_AreRejected()
    {
        var scheduler = new UpcomingMeetupScheduler(new CalendarDbFactory());
        await Assert.ThrowsAsync<ArgumentException>(() => scheduler.CreateDraftsAsync(
            Guid.NewGuid(), [], new(18, 30), 90, EventFormat.Hybrid, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => scheduler.CreateDraftsAsync(
            Guid.NewGuid(), [new(2000, 1, 1)], new(18, 30), 90, EventFormat.Hybrid, null, null));
    }

    [BookingSqlFact]
    public async Task ConcurrentBulkSubmissions_CreateOnlyOneBatch()
    {
        await using var database = await BookingSqlDatabase.CreateAsync();
        var factory = new CalendarDbFactory(database.Options);
        var group = new MeetupGroup { Name = "Concurrent group" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(group);
            await db.SaveChangesAsync();
        }
        var scheduler = new UpcomingMeetupScheduler(factory);
        async Task<int> CreateAsync()
        {
            try
            {
                return await scheduler.CreateDraftsAsync(group.Id,
                    [new(2032, 1, 15), new(2032, 2, 15)], new(18, 30), 90, EventFormat.Hybrid, "Online", null);
            }
            catch (InvalidOperationException ex)
            {
                Assert.Contains("already has meetups", ex.Message);
                return 0;
            }
        }
        var results = await Task.WhenAll(CreateAsync(), CreateAsync());
        Assert.Equal(new[] { 0, 2 }, results.Order().ToArray());
        await using var check = factory.CreateDbContext();
        Assert.Equal(2, await check.MeetupEvents.CountAsync());
    }
}

internal sealed class CalendarDbFactory(DbContextOptions<AppDbContext>? options = null) : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> options = options ??
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    public AppDbContext CreateDbContext() => new(options);
}
