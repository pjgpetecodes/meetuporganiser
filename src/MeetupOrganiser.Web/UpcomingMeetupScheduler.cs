using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Web;

public sealed class UpcomingMeetupScheduler(IDbContextFactory<AppDbContext> dbContextFactory)
{
    public async Task<int> CreateDraftsAsync(Guid groupId, IReadOnlyCollection<DateOnly> dates,
        TimeOnly startTime, int durationMinutes, EventFormat format, string? location, string? venue,
        CancellationToken cancellationToken = default)
    {
        if (dates.Count == 0)
            throw new ArgumentException("Select at least one meetup date.");
        if (durationMinutes is < 1 or > 1440)
            throw new ArgumentException("Duration must be between 1 and 1440 minutes.");
        if (!Enum.IsDefined(format))
            throw new ArgumentException("Select a valid meetup format.");
        if (location?.Length > 200 || venue?.Length > 100)
            throw new ArgumentException("Location must be at most 200 characters and venue at most 100 characters.");

        var starts = dates.Distinct().Order().Select(date =>
        {
            var local = date.ToDateTime(startTime);
            var utc = MeetupDateTime.ToUtc(local);
            if (utc <= DateTime.UtcNow)
                throw new ArgumentException($"{date:d MMM yyyy} must have a future start time.");
            return (Date: date, Local: local, Utc: utc);
        }).ToArray();

        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            // Serialize bulk requests for a group so double submissions cannot create duplicate dates.
            var group = db.Database.IsSqlServer()
                ? await db.MeetupGroups.FromSqlInterpolated(
                    $"SELECT * FROM [MeetupGroups] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {groupId}")
                    .SingleOrDefaultAsync(cancellationToken)
                : await db.MeetupGroups.SingleOrDefaultAsync(g => g.Id == groupId, cancellationToken);
            if (group is null)
                throw new InvalidOperationException("The selected meetup group no longer exists. Reload the page.");

            var existingStarts = await db.MeetupEvents.Where(e => e.MeetupGroupId == groupId)
                .Select(e => e.EventDateTimeUtc).ToListAsync(cancellationToken);
            var occupied = existingStarts.Select(utc => DateOnly.FromDateTime(EventHeader.ToUkTime(utc))).ToHashSet();
            var conflicts = starts.Where(s => occupied.Contains(s.Date)).Select(s => s.Date.ToString("d MMM yyyy")).ToArray();
            if (conflicts.Length > 0)
                throw new InvalidOperationException($"This group already has meetups on {string.Join(", ", conflicts)}. Remove those dates from your selection. No drafts were created.");

            db.MeetupEvents.AddRange(starts.Select(start =>
            {
                var title = EventTitles.Placeholder(group.Name, start.Local);
                if (title.Length > 250)
                    title = title[..(250 - EventTitles.PlaceholderSuffix.Length)] + EventTitles.PlaceholderSuffix;
                return new MeetupEvent
                {
                    MeetupGroupId = groupId,
                    Title = title,
                    EventDateTimeUtc = start.Utc,
                    DurationMinutes = durationMinutes,
                    Format = format,
                    LocationOrPlatform = location?.Trim(),
                    VenueName = venue?.Trim(),
                    ShortBitlyUrl = ShortUrl.ForEvent(group.ShortUrlStem, start.Utc),
                    Status = EventStatus.Draft,
                    IsOpenForBookings = true
                };
            }));
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return starts.Length;
        });
    }
}
