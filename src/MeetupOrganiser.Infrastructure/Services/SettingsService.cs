using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(AppDbContext dbContext, IConfiguration configuration, ILogger<SettingsService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbSetting = await _dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (dbSetting != null && !string.IsNullOrWhiteSpace(dbSetting.Value))
            {
                return dbSetting.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read setting {Key} from database. Falling back to IConfiguration.", key);
        }

        return _configuration[key];
    }

    public async Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var settings = await _dbContext.AppSettings
                .AsNoTracking()
                .Where(s => s.Category == category)
                .ToListAsync(cancellationToken);

            foreach (var setting in settings)
            {
                result[setting.Key] = !string.IsNullOrWhiteSpace(setting.Value)
                    ? setting.Value
                    : _configuration[setting.Key];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read settings for category {Category} from database.", category);
        }

        return result;
    }

    public async Task<List<AppSetting>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _dbContext.AppSettings.OrderBy(s => s.Category).ThenBy(s => s.Key).ToListAsync(cancellationToken);
            if (settings.Count == 0)
            {
                settings = GetDefaultSettings();
                _dbContext.AppSettings.AddRange(settings);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load all settings from database.");
            return GetDefaultSettings();
        }
    }

    public async Task SaveSettingsAsync(Dictionary<string, string?> settings, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.AppSettings.ToListAsync(cancellationToken);
        var existingDict = existing.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

        var defaults = GetDefaultSettings().ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in settings)
        {
            if (existingDict.TryGetValue(key, out var current))
            {
                current.Value = value;
                current.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                var def = defaults.TryGetValue(key, out var d) ? d : null;
                var newSetting = new AppSetting
                {
                    Key = key,
                    Value = value,
                    Category = def?.Category ?? "Custom",
                    Description = def?.Description ?? key,
                    IsSecret = def?.IsSecret ?? key.Contains("Secret", StringComparison.OrdinalIgnoreCase) || key.Contains("Password", StringComparison.OrdinalIgnoreCase) || key.Contains("Key", StringComparison.OrdinalIgnoreCase),
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _dbContext.AppSettings.Add(newSetting);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved {Count} application settings to database.", settings.Count);
    }

    public async Task<IntegrationSettingsModel> GetIntegrationSettingsAsync(CancellationToken cancellationToken = default)
    {
        return new IntegrationSettingsModel
        {
            BlueSkyHandle = await GetSettingAsync("Integrations:BlueSky:Handle", cancellationToken),
            BlueSkyAppPassword = await GetSettingAsync("Integrations:BlueSky:AppPassword", cancellationToken),
            ZoomAccountId = await GetSettingAsync("Integrations:Zoom:AccountId", cancellationToken),
            ZoomClientId = await GetSettingAsync("Integrations:Zoom:ClientId", cancellationToken),
            ZoomClientSecret = await GetSettingAsync("Integrations:Zoom:ClientSecret", cancellationToken),
            MeetupUrlname = await GetSettingAsync("Integrations:Meetup:Urlname", cancellationToken) ?? "dotnetnotts",
            MeetupApiKey = await GetSettingAsync("Integrations:Meetup:ApiKey", cancellationToken),
            SessionizeDefaultEventId = await GetSettingAsync("Integrations:Sessionize:DefaultEventId", cancellationToken),
            TwitterApiKey = await GetSettingAsync("Integrations:Twitter:ApiKey", cancellationToken),
            TwitterApiSecret = await GetSettingAsync("Integrations:Twitter:ApiSecret", cancellationToken),
            TwitterAccessToken = await GetSettingAsync("Integrations:Twitter:AccessToken", cancellationToken),
            TwitterAccessTokenSecret = await GetSettingAsync("Integrations:Twitter:AccessTokenSecret", cancellationToken),
            LinkedInClientId = await GetSettingAsync("Integrations:LinkedIn:ClientId", cancellationToken),
            LinkedInClientSecret = await GetSettingAsync("Integrations:LinkedIn:ClientSecret", cancellationToken),
            LinkedInAccessToken = await GetSettingAsync("Integrations:LinkedIn:AccessToken", cancellationToken),
            LinkedInAuthorUrn = await GetSettingAsync("Integrations:LinkedIn:AuthorUrn", cancellationToken)
        };
    }

    public async Task SaveIntegrationSettingsAsync(IntegrationSettingsModel model, CancellationToken cancellationToken = default)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Integrations:BlueSky:Handle"] = model.BlueSkyHandle,
            ["Integrations:BlueSky:AppPassword"] = model.BlueSkyAppPassword,
            ["Integrations:Zoom:AccountId"] = model.ZoomAccountId,
            ["Integrations:Zoom:ClientId"] = model.ZoomClientId,
            ["Integrations:Zoom:ClientSecret"] = model.ZoomClientSecret,
            ["Integrations:Meetup:Urlname"] = model.MeetupUrlname,
            ["Integrations:Meetup:ApiKey"] = model.MeetupApiKey,
            ["Integrations:Sessionize:DefaultEventId"] = model.SessionizeDefaultEventId,
            ["Integrations:Twitter:ApiKey"] = model.TwitterApiKey,
            ["Integrations:Twitter:ApiSecret"] = model.TwitterApiSecret,
            ["Integrations:Twitter:AccessToken"] = model.TwitterAccessToken,
            ["Integrations:Twitter:AccessTokenSecret"] = model.TwitterAccessTokenSecret,
            ["Integrations:LinkedIn:ClientId"] = model.LinkedInClientId,
            ["Integrations:LinkedIn:ClientSecret"] = model.LinkedInClientSecret,
            ["Integrations:LinkedIn:AccessToken"] = model.LinkedInAccessToken,
            ["Integrations:LinkedIn:AuthorUrn"] = model.LinkedInAuthorUrn
        };

        await SaveSettingsAsync(dict, cancellationToken);
    }

    public static List<AppSetting> GetDefaultSettings()
    {
        return
        [
            // BlueSky
            new AppSetting { Key = "Integrations:BlueSky:Handle", Category = "BlueSky", Description = "BlueSky Handle (e.g. dotnetnotts.bsky.social)", IsSecret = false },
            new AppSetting { Key = "Integrations:BlueSky:AppPassword", Category = "BlueSky", Description = "BlueSky App Password", IsSecret = true },

            // Zoom
            new AppSetting { Key = "Integrations:Zoom:AccountId", Category = "Zoom", Description = "Zoom Account ID for Server-to-Server OAuth", IsSecret = false },
            new AppSetting { Key = "Integrations:Zoom:ClientId", Category = "Zoom", Description = "Zoom OAuth Client ID", IsSecret = false },
            new AppSetting { Key = "Integrations:Zoom:ClientSecret", Category = "Zoom", Description = "Zoom OAuth Client Secret", IsSecret = true },

            // Meetup
            new AppSetting { Key = "Integrations:Meetup:Urlname", Category = "Meetup", Description = "Meetup Group URL name (e.g. dotnetnotts)", IsSecret = false },
            new AppSetting { Key = "Integrations:Meetup:ApiKey", Category = "Meetup", Description = "Meetup API Key or OAuth Token", IsSecret = true },

            // Sessionize
            new AppSetting { Key = "Integrations:Sessionize:DefaultEventId", Category = "Sessionize", Description = "Default Sessionize Event ID or Endpoint", IsSecret = false },

            // Twitter / X
            new AppSetting { Key = "Integrations:Twitter:ApiKey", Category = "Twitter / X", Description = "Twitter API / Consumer Key", IsSecret = false },
            new AppSetting { Key = "Integrations:Twitter:ApiSecret", Category = "Twitter / X", Description = "Twitter API / Consumer Secret", IsSecret = true },
            new AppSetting { Key = "Integrations:Twitter:AccessToken", Category = "Twitter / X", Description = "Twitter Access Token", IsSecret = false },
            new AppSetting { Key = "Integrations:Twitter:AccessTokenSecret", Category = "Twitter / X", Description = "Twitter Access Token Secret", IsSecret = true },

            // LinkedIn
            new AppSetting { Key = "Integrations:LinkedIn:ClientId", Category = "LinkedIn", Description = "LinkedIn Client ID", IsSecret = false },
            new AppSetting { Key = "Integrations:LinkedIn:ClientSecret", Category = "LinkedIn", Description = "LinkedIn Client Secret", IsSecret = true },
            new AppSetting { Key = "Integrations:LinkedIn:AccessToken", Category = "LinkedIn", Description = "LinkedIn OAuth Access Token", IsSecret = true },
            new AppSetting { Key = "Integrations:LinkedIn:AuthorUrn", Category = "LinkedIn", Description = "LinkedIn Author URN (e.g. urn:li:organization:... or urn:li:person:...)", IsSecret = false },
        ];
    }
}
