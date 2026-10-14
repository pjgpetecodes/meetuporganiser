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
    private const string LinkedInApiVersion = "202610";
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

    public async Task<SocialPostResult> PostToLinkedInAsync(string text, byte[]? imageBytes, CancellationToken cancellationToken = default)
    {
        var intentUrl = $"https://www.linkedin.com/sharing/share-offsite/?text={Uri.EscapeDataString(text)}";
        var accessToken = (_settingsService != null
                ? await _settingsService.GetSettingAsync("Integrations:LinkedIn:AccessToken", cancellationToken)
                : null)
            ?? _configuration["Integrations:LinkedIn:AccessToken"];
        var authorUrn = (_settingsService != null
                ? await _settingsService.GetSettingAsync("Integrations:LinkedIn:AuthorUrn", cancellationToken)
                : null)
            ?? _configuration["Integrations:LinkedIn:AuthorUrn"];

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(authorUrn))
        {
            return new SocialPostResult(
                "LinkedIn",
                false,
                null,
                null,
                intentUrl,
                text,
                "LinkedIn API posting requires an access token with w_member_social and your personal author URN (urn:li:person:...).");
        }

        authorUrn = authorUrn.Trim();
        if (!authorUrn.StartsWith("urn:li:person:", StringComparison.Ordinal))
        {
            return new SocialPostResult(
                "LinkedIn",
                false,
                null,
                null,
                intentUrl,
                text,
                "For personal profile posts, the LinkedIn Author URN must start with urn:li:person:.");
        }

        try
        {
            string? imageUrn = null;
            if (imageBytes is { Length: > 0 })
            {
                imageUrn = await UploadLinkedInImageAsync(accessToken, authorUrn, imageBytes, cancellationToken);
            }

            var post = new Dictionary<string, object>
            {
                ["author"] = authorUrn,
                ["commentary"] = text,
                ["visibility"] = "PUBLIC",
                ["distribution"] = new
                {
                    feedDistribution = "MAIN_FEED",
                    targetEntities = Array.Empty<string>(),
                    thirdPartyDistributionChannels = Array.Empty<string>()
                },
                ["lifecycleState"] = "PUBLISHED",
                ["isReshareDisabledByAuthor"] = false
            };

            if (imageUrn != null)
            {
                post["content"] = new
                {
                    media = new { id = imageUrn }
                };
            }

            using var request = CreateLinkedInRequest(HttpMethod.Post, "https://api.linkedin.com/rest/posts", accessToken);
            request.Content = JsonContent.Create(post);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return new SocialPostResult(
                    "LinkedIn",
                    false,
                    null,
                    null,
                    intentUrl,
                    text,
                    $"LinkedIn post failed ({(int)response.StatusCode}): {error}");
            }

            response.Headers.TryGetValues("x-restli-id", out var postIds);
            var postId = postIds?.FirstOrDefault();
            var postUrl = string.IsNullOrWhiteSpace(postId)
                ? null
                : $"https://www.linkedin.com/feed/update/{Uri.EscapeDataString(postId)}/";
            return new SocialPostResult("LinkedIn", true, postId, postUrl, intentUrl, text, null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LinkedIn API request failed.");
            return new SocialPostResult("LinkedIn", false, null, null, intentUrl, text, $"LinkedIn API request failed: {ex.Message}");
        }
    }

    private async Task<string> UploadLinkedInImageAsync(
        string accessToken,
        string authorUrn,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        using var initializeRequest = CreateLinkedInRequest(
            HttpMethod.Post,
            "https://api.linkedin.com/rest/images?action=initializeUpload",
            accessToken);
        initializeRequest.Content = JsonContent.Create(new
        {
            initializeUploadRequest = new { owner = authorUrn }
        });

        using var initializeResponse = await _httpClient.SendAsync(initializeRequest, cancellationToken);
        if (!initializeResponse.IsSuccessStatusCode)
        {
            var error = await initializeResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"LinkedIn image upload initialization failed ({(int)initializeResponse.StatusCode}): {error}");
        }

        using var responseJson = await JsonDocument.ParseAsync(
            await initializeResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var uploadValue = responseJson.RootElement.GetProperty("value");
        var uploadUri = uploadValue.GetProperty("uploadUrl").GetString();
        var imageUrn = uploadValue.GetProperty("image").GetString();

        if (!Uri.TryCreate(uploadUri, UriKind.Absolute, out var parsedUploadUri)
            || parsedUploadUri.Scheme != Uri.UriSchemeHttps
            || !(parsedUploadUri.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase)
                || parsedUploadUri.Host.EndsWith(".licdn.com", StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(imageUrn))
        {
            throw new HttpRequestException("LinkedIn returned an invalid image upload URL or image URN.");
        }

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, parsedUploadUri)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var error = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LinkedIn image upload failed ({(int)uploadResponse.StatusCode}): {error}");
        }

        return imageUrn;
    }

    private static HttpRequestMessage CreateLinkedInRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Linkedin-Version", LinkedInApiVersion);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        return request;
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
