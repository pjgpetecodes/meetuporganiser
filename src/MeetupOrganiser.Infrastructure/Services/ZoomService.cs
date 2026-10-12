using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class ZoomService : IZoomService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ZoomService> _logger;

    public ZoomService(HttpClient httpClient, IConfiguration configuration, ILogger<ZoomService> logger)
        : this(httpClient, null, configuration, logger)
    {
    }

    public ZoomService(HttpClient httpClient, ISettingsService? settingsService, IConfiguration configuration, ILogger<ZoomService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ZoomMeetingResult> CreateMeetingAsync(string topic, DateTime startTimeUtc, int durationMinutes, string? agenda = null, CancellationToken cancellationToken = default)
    {
        var accountId = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Zoom:AccountId", cancellationToken) : null)
            ?? _configuration["Integrations:Zoom:AccountId"];
        var clientId = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Zoom:ClientId", cancellationToken) : null)
            ?? _configuration["Integrations:Zoom:ClientId"];
        var clientSecret = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Zoom:ClientSecret", cancellationToken) : null)
            ?? _configuration["Integrations:Zoom:ClientSecret"];

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogInformation("Zoom credentials not configured. Generating placeholder meeting link.");
            var fakeId = Random.Shared.Next(100000000, 999999999).ToString();
            return new ZoomMeetingResult(
                Success: true,
                MeetingId: fakeId,
                JoinUrl: $"https://us02web.zoom.us/j/{fakeId}?pwd=meetup{Random.Shared.Next(1000, 9999)}",
                Passcode: $"meetup{Random.Shared.Next(1000, 9999)}",
                ErrorMessage: null
            );
        }

        try
        {
            // 1. Get OAuth Access Token via Server-to-Server flow
            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"https://zoom.us/oauth/token?grant_type=account_credentials&account_id={accountId}");
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

            var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var err = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                return new ZoomMeetingResult(false, null, null, null, $"Zoom Auth failed: {tokenResponse.StatusCode} - {err}");
            }

            var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var accessToken = tokenJson.GetProperty("access_token").GetString();

            // 2. Create Meeting
            var meetingPayload = new
            {
                topic = topic,
                type = 2, // Scheduled meeting
                start_time = startTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                duration = durationMinutes,
                timezone = "UTC",
                agenda = agenda ?? topic,
                settings = new
                {
                    host_video = true,
                    participant_video = true,
                    join_before_host = false,
                    mute_upon_entry = true,
                    waiting_room = false
                }
            };

            var meetingRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.zoom.us/v2/users/me/meetings")
            {
                Content = new StringContent(JsonSerializer.Serialize(meetingPayload), Encoding.UTF8, "application/json")
            };
            meetingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var meetingResponse = await _httpClient.SendAsync(meetingRequest, cancellationToken);
            if (!meetingResponse.IsSuccessStatusCode)
            {
                var err = await meetingResponse.Content.ReadAsStringAsync(cancellationToken);
                return new ZoomMeetingResult(false, null, null, null, $"Zoom Meeting creation failed: {err}");
            }

            var resultJson = await meetingResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return new ZoomMeetingResult(
                Success: true,
                MeetingId: resultJson.GetProperty("id").GetInt64().ToString(),
                JoinUrl: resultJson.GetProperty("join_url").GetString(),
                Passcode: resultJson.TryGetProperty("password", out var pwd) ? pwd.GetString() : null,
                ErrorMessage: null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating Zoom meeting.");
            return new ZoomMeetingResult(false, null, null, null, ex.Message);
        }
    }
}
