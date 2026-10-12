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
        Assert.True(result.Success);
        Assert.NotNull(result.EventUrl);
        Assert.Contains("azure-notts", result.EventUrl);
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
        Assert.True(result.Success);
        Assert.NotNull(result.EventUrl);
        Assert.Contains("global-fallback-slug", result.EventUrl);
    }
}
