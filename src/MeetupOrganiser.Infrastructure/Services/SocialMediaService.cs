using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class SocialMediaService : ISocialMediaService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SocialMediaService> _logger;

    public SocialMediaService(HttpClient httpClient, IConfiguration configuration, ILogger<SocialMediaService> logger)
        : this(httpClient, null, configuration, logger)
    {
    }

    public SocialMediaService(HttpClient httpClient, ISettingsService? settingsService, IConfiguration configuration, ILogger<SocialMediaService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SocialPostResult> PostToBlueSkyAsync(string text, byte[]? imageBytes, string? imageMimeType, CancellationToken cancellationToken = default)
    {
        var handle = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:BlueSky:Handle", cancellationToken) : null)
            ?? _configuration["Integrations:BlueSky:Handle"];
        var appPassword = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:BlueSky:AppPassword", cancellationToken) : null)
            ?? _configuration["Integrations:BlueSky:AppPassword"];
        var intentUrl = $"https://bsky.app/intent/compose?text={Uri.EscapeDataString(text)}";

        if (string.IsNullOrWhiteSpace(handle) || string.IsNullOrWhiteSpace(appPassword))
        {
            return new SocialPostResult("BlueSky", true, null, null, intentUrl, text, "BlueSky credentials not configured. Use web intent link to publish.");
        }

        try
        {
            // 1. Authenticate with AT Protocol
            var sessionRequest = new
            {
                identifier = handle,
                password = appPassword
            };
            var authResp = await _httpClient.PostAsJsonAsync("https://bsky.social/xrpc/com.atproto.server.createSession", sessionRequest, cancellationToken);
            if (!authResp.IsSuccessStatusCode)
            {
                var err = await authResp.Content.ReadAsStringAsync(cancellationToken);
                return new SocialPostResult("BlueSky", false, null, null, intentUrl, text, $"BlueSky auth error: {err}");
            }

            var authJson = await authResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var jwt = authJson.GetProperty("accessJwt").GetString();
            var did = authJson.GetProperty("did").GetString();

            JsonElement? blobRef = null;
            if (imageBytes != null && imageBytes.Length > 0)
            {
                var uploadReq = new HttpRequestMessage(HttpMethod.Post, "https://bsky.social/xrpc/com.atproto.repo.uploadBlob")
                {
                    Content = new ByteArrayContent(imageBytes)
                };
                uploadReq.Content.Headers.ContentType = new MediaTypeHeaderValue(imageMimeType ?? "image/png");
                uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

                var uploadResp = await _httpClient.SendAsync(uploadReq, cancellationToken);
                if (uploadResp.IsSuccessStatusCode)
                {
                    var blobJson = await uploadResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                    blobRef = blobJson.GetProperty("blob");
                }
            }

            // 2. Create Post Record
            var postRecord = new Dictionary<string, object>
            {
                ["$type"] = "app.bsky.feed.post",
                ["text"] = text,
                ["createdAt"] = DateTime.UtcNow.ToString("o")
            };

            if (blobRef.HasValue)
            {
                postRecord["embed"] = new
                {
                    @type = "app.bsky.embed.images",
                    images = new[]
                    {
                        new
                        {
                            alt = "Meetup Social Card",
                            image = blobRef.Value
                        }
                    }
                };
            }

            var recordReq = new
            {
                repo = did,
                collection = "app.bsky.feed.post",
                record = postRecord
            };

            var postReq = new HttpRequestMessage(HttpMethod.Post, "https://bsky.social/xrpc/com.atproto.repo.createRecord")
            {
                Content = new StringContent(JsonSerializer.Serialize(recordReq), Encoding.UTF8, "application/json")
            };
            postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            var postResp = await _httpClient.SendAsync(postReq, cancellationToken);
            if (!postResp.IsSuccessStatusCode)
            {
                var err = await postResp.Content.ReadAsStringAsync(cancellationToken);
                return new SocialPostResult("BlueSky", false, null, null, intentUrl, text, $"BlueSky post error: {err}");
            }

            var postResultJson = await postResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var uri = postResultJson.GetProperty("uri").GetString();
            return new SocialPostResult("BlueSky", true, uri, $"https://bsky.app/profile/{handle}", intentUrl, text, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post to BlueSky.");
            return new SocialPostResult("BlueSky", false, null, null, intentUrl, text, ex.Message);
        }
    }

    public Task<SocialPostResult> PostToTwitterAsync(string text, byte[]? imageBytes, CancellationToken cancellationToken = default)
    {
        var intentUrl = $"https://twitter.com/intent/tweet?text={Uri.EscapeDataString(text)}";
        return Task.FromResult(new SocialPostResult("Twitter", true, null, null, intentUrl, text, null));
    }

    public Task<SocialPostResult> PostToLinkedInAsync(string text, byte[]? imageBytes, CancellationToken cancellationToken = default)
    {
        var intentUrl = $"https://www.linkedin.com/sharing/share-offsite/?text={Uri.EscapeDataString(text)}";
        return Task.FromResult(new SocialPostResult("LinkedIn", true, null, null, intentUrl, text, null));
    }

    public string GeneratePlatformPostText(MeetupEvent meetupEvent, string platform) =>
        SocialPostFormatter.Render(meetupEvent, platform, meetupEvent.MeetupGroup?.SocialPostTemplate);

    public string GetPlatformWebIntentUrl(MeetupEvent meetupEvent, string platform)
    {
        var text = GeneratePlatformPostText(meetupEvent, platform);
        var url = meetupEvent.ShortBitlyUrl ?? meetupEvent.MeetupEventUrl ?? "https://meetup.com";

        return platform.ToLowerInvariant() switch
        {
            "twitter" or "x" => $"https://twitter.com/intent/tweet?text={Uri.EscapeDataString(text)}",
            "bluesky" => $"https://bsky.app/intent/compose?text={Uri.EscapeDataString(text)}",
            "linkedin" => $"https://www.linkedin.com/feed/?shareActive=true&text={Uri.EscapeDataString(text)}",
            _ => url
        };
    }
}
