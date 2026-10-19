using System.Net;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class BankHolidayServiceTests
{
    private const string Feed = """
        {
          "england-and-wales": {
            "events": [
              { "title": "New Year’s Day", "date": "2027-01-01", "notes": "" },
              { "title": "Christmas Day", "date": "2027-12-27", "notes": "Substitute day" },
              { "title": "Bank holiday for the coronation", "date": "2023-05-08", "notes": "" }
            ]
          },
          "scotland": {
            "events": [{ "title": "St Andrew’s Day", "date": "2027-11-30", "notes": "" }]
          }
        }
        """;

    [Fact]
    public async Task OfficialFeed_UsesOnlyEnglandAndWalesAndPreservesSubstituteAndExceptionalHolidays()
    {
        using var handler = new FeedHandler(Feed);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new BankHolidayService(client, cache, NullLogger<BankHolidayService>.Instance);

        var calendar = await service.GetCalendarAsync();
        Assert.True(calendar.IsAvailable);
        Assert.True(calendar.CoversYear(2027));
        Assert.True(calendar.CoversYear(2023));
        Assert.False(calendar.CoversYear(2024));
        Assert.False(calendar.CoversYear(2032));
        Assert.Null(calendar.Find(new(2027, 11, 30)));
        Assert.Equal("Christmas Day (Substitute day)", calendar.Find(new(2027, 12, 27))?.Description);
        Assert.Equal("Bank holiday for the coronation", calendar.Find(new(2023, 5, 8))?.Title);
        Assert.Same(calendar, await service.GetCalendarAsync());
        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://www.gov.uk/bank-holidays.json", handler.RequestUri?.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"england-and-wales\":null}")]
    [InlineData("{\"england-and-wales\":{\"events\":[]}}")]
    [InlineData("{\"england-and-wales\":{\"events\":[null]}}")]
    [InlineData("{\"england-and-wales\":{\"events\":[{\"date\":\"2027-02-30\",\"title\":\"Invalid\"}]}}")]
    [InlineData("{\"england-and-wales\":{\"events\":[{\"date\":\"2027-1-1\",\"title\":\"Invalid\"}]}}")]
    [InlineData("{\"england-and-wales\":{\"events\":[{\"date\":\"2027-01-01\",\"title\":\" \"}]}}")]
    [InlineData("{\"england-and-wales\":{\"events\":[{\"date\":\"2027-01-01\",\"title\":\"A\"},{\"date\":\"2027-01-01\",\"title\":\"B\"}]}}")]
    [InlineData("not json")]
    public async Task InvalidFeed_IsExplicitlyUnavailableAndBrieflyCached(string json)
    {
        using var handler = new FeedHandler(json);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new BankHolidayService(client, cache, NullLogger<BankHolidayService>.Instance);

        var calendar = await service.GetCalendarAsync();
        Assert.False(calendar.IsAvailable);
        Assert.Same(calendar, await service.GetCalendarAsync());
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpFailureOrTimeout_IsExplicitlyUnavailable(bool timeout)
    {
        using var handler = new FeedHandler(Feed, timeout ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, timeout);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var calendar = await new BankHolidayService(client, cache, NullLogger<BankHolidayService>.Instance).GetCalendarAsync();
        Assert.False(calendar.IsAvailable);
    }

    [Fact]
    public async Task ConcurrentPickers_ShareOneFeedFetchAcrossServiceInstances()
    {
        using var handler = new FeedHandler(Feed);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var first = new BankHolidayService(client, cache, NullLogger<BankHolidayService>.Instance);
        var second = new BankHolidayService(client, cache, NullLogger<BankHolidayService>.Instance);
        var results = await Task.WhenAll(first.GetCalendarAsync(), second.GetCalendarAsync());
        Assert.Same(results[0], results[1]);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsUnavailable()
    {
        using var handler = new FeedHandler(Feed);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var service = new BankHolidayService(client, cache, NullLogger<BankHolidayService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetCalendarAsync(cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class FeedHandler(string json, HttpStatusCode status = HttpStatusCode.OK, bool timeout = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            await Task.Yield();
            if (timeout) throw new TaskCanceledException("Simulated HTTP timeout.");
            return new(status) { Content = new StringContent(json) };
        }
    }
}
