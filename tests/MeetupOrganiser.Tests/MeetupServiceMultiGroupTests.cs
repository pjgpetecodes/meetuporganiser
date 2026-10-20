using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class MeetupServiceMultiGroupTests
{
    private IConfiguration CreateConfig(string? apiKey = null, string? urlname = "global-slug")
    {
        var dict = new Dictionary<string, string?>
        {
            ["Integrations:Meetup:ApiKey"] = apiKey,
            ["Integrations:Meetup:Urlname"] = urlname
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task CreateEventAsync_UsesGroupSpecificSlug_WhenProvided()
    {
        // Arrange
        var config = CreateConfig();
        var httpClient = new HttpClient();
        var service = new MeetupService(httpClient, config, NullLogger<MeetupService>.Instance);

        var group = new MeetupGroup
        {
            Name = "Azure User Group",
            MeetupUrlName = "azure-notts"
        };

        var evt = new MeetupEvent
        {
            Title = "Serverless Cloud Meetup",
            EventDateTimeUtc = DateTime.UtcNow.AddDays(7),
            MeetupGroup = group
        };

        // Act
        var result = await service.CreateEventAsync(evt);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.RequiresManualAction);
        Assert.Null(result.EventId);
        Assert.NotNull(result.EventUrl);
        Assert.Equal("https://www.meetup.com/azure-notts/", result.EventUrl);
    }

    [Fact]
    public async Task CreateEventAsync_FallsBackToGlobalSlug_WhenGroupSlugEmpty()
    {
        // Arrange
        var config = CreateConfig(urlname: "global-fallback-slug");
        var httpClient = new HttpClient();
        var service = new MeetupService(httpClient, config, NullLogger<MeetupService>.Instance);

        var group = new MeetupGroup
        {
            Name = "Community Group",
            MeetupUrlName = null
        };

        var evt = new MeetupEvent
        {
            Title = "Community Gathering",
            EventDateTimeUtc = DateTime.UtcNow.AddDays(14),
            MeetupGroup = group
        };

        // Act
        var result = await service.CreateEventAsync(evt);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.RequiresManualAction);
        Assert.Null(result.EventId);
        Assert.NotNull(result.EventUrl);
        Assert.Equal("https://www.meetup.com/global-fallback-slug/", result.EventUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../another-group")]
    [InlineData("https://example.com")]
    public async Task CreateEventAsync_RejectsMissingOrInvalidGroupRatherThanUsingAnotherGroup(string? slug)
    {
        var service = new MeetupService(new HttpClient(), CreateConfig(urlname: slug), NullLogger<MeetupService>.Instance);

        var result = await service.CreateEventAsync(new MeetupEvent());

        Assert.False(result.Success);
        Assert.False(result.RequiresManualAction);
        Assert.Null(result.EventUrl);
        Assert.Contains("group URL name", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateEventAsync_WithApiAccessStillReturnsConfirmedEvent()
    {
        using var handler = new CreationHandler();
        using var client = new HttpClient(handler);
        var service = new MeetupService(client, CreateConfig(apiKey: "test-access-token"), NullLogger<MeetupService>.Instance);

        var result = await service.CreateEventAsync(new MeetupEvent { Title = "Community talk" });

        Assert.True(result.Success);
        Assert.False(result.RequiresManualAction);
        Assert.Equal("123", result.EventId);
        Assert.Equal("https://www.meetup.com/global-slug/events/123/", result.EventUrl);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class CreationHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.meetup.com/gql-ext", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"createEvent":{"event":{"id":"123","eventUrl":"https://www.meetup.com/global-slug/events/123/"},"errors":[]}}}""")
            });
        }
    }
}
