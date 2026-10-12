using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface ISettingsService
{
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(string category, CancellationToken cancellationToken = default);
    Task<List<AppSetting>> GetAllSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(Dictionary<string, string?> settings, CancellationToken cancellationToken = default);
    Task<IntegrationSettingsModel> GetIntegrationSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveIntegrationSettingsAsync(IntegrationSettingsModel model, CancellationToken cancellationToken = default);
}
