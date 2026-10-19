using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public sealed class BankHolidayService(HttpClient httpClient, IMemoryCache cache, ILogger<BankHolidayService> logger)
    : IBankHolidayService
{
    private const string CacheKey = "BankHolidays:EnglandAndWales";
    private const string FeedUrl = "https://www.gov.uk/bank-holidays.json";
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    public async Task<BankHolidayCalendar> GetCalendarAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<BankHolidayCalendar>(CacheKey, out var cached) && cached is not null)
            return cached;

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<BankHolidayCalendar>(CacheKey, out cached) && cached is not null)
                return cached;
            return await LoadCalendarAsync(cancellationToken);
        }
        finally { RefreshLock.Release(); }
    }

    private async Task<BankHolidayCalendar> LoadCalendarAsync(CancellationToken cancellationToken)
    {
        BankHolidayCalendar result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await httpClient.GetAsync(FeedUrl, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var feed = await JsonSerializer.DeserializeAsync<Dictionary<string, HolidayDivision>>(stream, cancellationToken: timeout.Token);
            if (feed is null || !feed.TryGetValue("england-and-wales", out var division) || division?.Events is not { Length: > 0 })
                throw new JsonException("The official feed has no England-and-Wales holiday data.");

            var holidays = division.Events.Select(item =>
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Title) ||
                    !DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new JsonException("The official feed contains an invalid holiday.");
                return new BankHoliday(date, item.Title.Trim(), item.Notes?.Trim());
            }).ToArray();
            if (holidays.Select(h => h.Date).Distinct().Count() != holidays.Length)
                throw new JsonException("The official feed contains duplicate holiday dates.");
            result = new BankHolidayCalendar(holidays);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not retrieve official England-and-Wales bank holidays.");
            result = BankHolidayCalendar.Unavailable;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Official England-and-Wales bank holiday data could not be read.");
            result = BankHolidayCalendar.Unavailable;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Official England-and-Wales bank holiday response was interrupted.");
            result = BankHolidayCalendar.Unavailable;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Official England-and-Wales bank holiday request timed out.");
            result = BankHolidayCalendar.Unavailable;
        }

        cache.Set(CacheKey, result, result.IsAvailable ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(1));
        return result;
    }

    private sealed record HolidayDivision([property: JsonPropertyName("events")] HolidayEntry?[]? Events);
    private sealed record HolidayEntry(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("notes")] string? Notes);
}
