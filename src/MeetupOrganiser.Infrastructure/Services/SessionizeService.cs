using System.Net.Http.Json;
using System.Text.Json;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class SessionizeService : ISessionizeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SessionizeService> _logger;

    public SessionizeService(HttpClient httpClient, ILogger<SessionizeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<SessionizeSpeakerDto>> GetSpeakersAsync(string endpointOrEventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = endpointOrEventId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? endpointOrEventId
                : $"https://sessionize.com/api/v2/{endpointOrEventId}/view/Speakers";

            _logger.LogInformation("Fetching speakers from Sessionize URL: {Url}", url);
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sessionize API returned status code {StatusCode}", response.StatusCode);
                return [];
            }

            var speakers = await response.Content.ReadFromJsonAsync<List<SessionizeSpeakerDto>>(cancellationToken: cancellationToken);
            return speakers ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull speakers from Sessionize.");
            return [];
        }
    }

    public async Task<List<SessionizeSessionDto>> GetSessionsAsync(string endpointOrEventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = endpointOrEventId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? endpointOrEventId
                : $"https://sessionize.com/api/v2/{endpointOrEventId}/view/Sessions";

            _logger.LogInformation("Fetching sessions from Sessionize URL: {Url}", url);
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sessionize API returned status code {StatusCode}", response.StatusCode);
                return [];
            }

            var sessions = await response.Content.ReadFromJsonAsync<List<SessionizeSessionDto>>(cancellationToken: cancellationToken);
            return sessions ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull sessions from Sessionize.");
            return [];
        }
    }
}
