using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class SettingsServiceTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private IConfiguration CreateConfiguration(Dictionary<string, string?> initialValues)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(initialValues)
            .Build();
    }

    [Fact]
    public async Task GetSettingAsync_ReturnsConfigValue_WhenDbEmpty()
    {
        using var context = CreateDbContext();
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Integrations:BlueSky:Handle"] = "config-handle.bsky.social"
        });
        var service = new SettingsService(context, config, NullLogger<SettingsService>.Instance);

        var result = await service.GetSettingAsync("Integrations:BlueSky:Handle");

        Assert.Equal("config-handle.bsky.social", result);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsToDatabase_And_OverridesConfiguration()
    {
        using var context = CreateDbContext();
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Integrations:BlueSky:Handle"] = "old-handle.bsky.social"
        });
        var service = new SettingsService(context, config, NullLogger<SettingsService>.Instance);

        // Act - Save new value to DB
        await service.SaveSettingsAsync(new Dictionary<string, string?>
        {
            ["Integrations:BlueSky:Handle"] = "new-handle.bsky.social"
        });

        // Act - Retrieve value
        var result = await service.GetSettingAsync("Integrations:BlueSky:Handle");

        // Assert - DB value takes precedence over config
        Assert.Equal("new-handle.bsky.social", result);
    }

    [Fact]
    public async Task GetAllSettingsAsync_SeedsDefaultSettings_WhenDbEmpty()
    {
        using var context = CreateDbContext();
        var config = CreateConfiguration(new Dictionary<string, string?>());
        var service = new SettingsService(context, config, NullLogger<SettingsService>.Instance);

        var settings = await service.GetAllSettingsAsync();

        Assert.NotEmpty(settings);
        Assert.Contains(settings, s => s.Key == "Integrations:BlueSky:Handle");
        Assert.Contains(settings, s => s.Key == "Integrations:Zoom:AccountId");
        Assert.Contains(settings, s => s.Key == "Integrations:Meetup:Urlname");
    }
}
