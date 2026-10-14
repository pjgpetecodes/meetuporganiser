using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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

    public async Task<SocialPostResult> PostToTwitterAsync(string text, byte[]? imageBytes, CancellationToken cancellationToken = default)
    {
        var intentUrl = $"https://twitter.com/intent/tweet?text={Uri.EscapeDataString(text)}";
        var apiKey = (_settingsService != null
                ? await _settingsService.GetSettingAsync("Integrations:Twitter:ApiKey", cancellationToken)
                : null)
            ?? _configuration["Integrations:Twitter:ApiKey"];
        var apiSecret = (_settingsService != null
                ? await _settingsService.GetSettingAsync("Integrations:Twitter:ApiSecret", cancellationToken)
                : null)
            ?? _configuration["Integrations:Twitter:ApiSecret"];
        var accessToken = (_settingsService != null
                ? await _settingsService.GetSettingAsync("Integrations:Twitter:AccessToken", cancellationToken)
                : null)
            ?? _configuration["Integrations:Twitter:AccessToken"];
        var accessTokenSecret = (_settingsService != null
                ? await _settingsService.GetSettingAsync("Integrations:Twitter:AccessTokenSecret", cancellationToken)
                : null)
            ?? _configuration["Integrations:Twitter:AccessTokenSecret"];

        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(apiSecret)
            || string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(accessTokenSecret))
        {
            return new SocialPostResult(
                "Twitter",
                false,
                null,
                null,
                intentUrl,
                text,
                "X API posting requires the API key, API secret, access token, and access token secret. Use Web Intent to compose the post instead.");
        }

        try
        {
            string? mediaId = null;
            if (imageBytes is { Length: > 0 })
            {
                var multipartContent = new MultipartFormDataContent();
                using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/media/upload")
                {
                    Content = multipartContent
                };
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                multipartContent.Add(imageContent, "media", "social-card.png");
                multipartContent.Add(new StringContent("tweet_image"), "media_category");
                uploadRequest.Headers.Authorization = CreateTwitterAuthorizationHeader(
                    HttpMethod.Post,
                    uploadRequest.RequestUri!,
                    apiKey,
                    apiSecret,
                    accessToken,
                    accessTokenSecret);

                using var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken);
                if (!uploadResponse.IsSuccessStatusCode)
                {
                    var error = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
                    return new SocialPostResult("Twitter", false, null, null, intentUrl, text, $"X media upload error: {error}");
                }

                using var uploadJson = await uploadResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
                var uploadData = uploadJson?.RootElement
                    .GetProperty("data")
                    ?? throw new InvalidOperationException("X media upload returned an empty response.");
                mediaId = uploadData.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(mediaId))
                {
                    throw new InvalidOperationException("X media upload response did not include a media ID.");
                }
            }

            var postBody = mediaId == null
                ? JsonSerializer.Serialize(new { text })
                : JsonSerializer.Serialize(new { text, media = new { media_ids = new[] { mediaId } } });
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/tweets")
            {
                Content = new StringContent(postBody, Encoding.UTF8, "application/json")
            };
            postRequest.Headers.Authorization = CreateTwitterAuthorizationHeader(
                HttpMethod.Post,
                postRequest.RequestUri!,
                apiKey,
                apiSecret,
                accessToken,
                accessTokenSecret);

            using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken);
            if (!postResponse.IsSuccessStatusCode)
            {
                var error = await postResponse.Content.ReadAsStringAsync(cancellationToken);
                return new SocialPostResult("Twitter", false, null, null, intentUrl, text, $"X post error: {error}");
            }

            using var postJson = await postResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            var postId = postJson?.RootElement.GetProperty("data").GetProperty("id").GetString()
                ?? throw new InvalidOperationException("X post response did not include a post ID.");
            return new SocialPostResult("Twitter", true, postId, $"https://x.com/i/status/{postId}", intentUrl, text, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to post to X.");
            return new SocialPostResult("Twitter", false, null, null, intentUrl, text, ex.Message);
        }
    }

    private static AuthenticationHeaderValue CreateTwitterAuthorizationHeader(
        HttpMethod method,
        Uri requestUri,
        string apiKey,
        string apiSecret,
        string accessToken,
        string accessTokenSecret)
    {
        var oauthParameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = apiKey,
            ["oauth_nonce"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["oauth_token"] = accessToken,
            ["oauth_version"] = "1.0"
        };
        var normalizedParameters = string.Join("&", oauthParameters.Select(parameter =>
            $"{OAuthEncode(parameter.Key)}={OAuthEncode(parameter.Value)}"));
        var signatureBase = $"{method.Method.ToUpperInvariant()}&{OAuthEncode(requestUri.GetLeftPart(UriPartial.Path))}&{OAuthEncode(normalizedParameters)}";
        var signingKey = $"{OAuthEncode(apiSecret)}&{OAuthEncode(accessTokenSecret)}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
        oauthParameters["oauth_signature"] = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase)));

        var authorization = "OAuth " + string.Join(", ", oauthParameters.Select(parameter =>
            $"{OAuthEncode(parameter.Key)}=\"{OAuthEncode(parameter.Value)}\""));
        return AuthenticationHeaderValue.Parse(authorization);
    }

    private static string OAuthEncode(string value) =>
        Uri.EscapeDataString(value)
            .Replace("!", "%21", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal)
            .Replace("'", "%27", StringComparison.Ordinal)
            .Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal);

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
