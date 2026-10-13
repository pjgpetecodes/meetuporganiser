using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class MeetupOAuthService : IMeetupOAuthService
{
    private const string TokenSettingKey = "Integrations:Meetup:OAuthTokenSet";
    private const string LegacyTokenSettingKey = "Integrations:Meetup:ApiKey";
    private const string AuthorizeEndpoint = "https://secure.meetup.com/oauth2/authorize";
    private const string TokenEndpoint = "https://secure.meetup.com/oauth2/access";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IConfiguration _configuration;
    private readonly IDataProtector _tokenProtector;
    private readonly ILogger<MeetupOAuthService> _logger;

    public MeetupOAuthService(
        HttpClient httpClient,
        ISettingsService settingsService,
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MeetupOAuthService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _configuration = configuration;
        _tokenProtector = dataProtectionProvider.CreateProtector("MeetupOrganiser.MeetupOAuthTokens.v1");
        _logger = logger;
    }

    public async Task<string> CreateAuthorizationUrlAsync(string state, CancellationToken cancellationToken = default)
    {
        var clientId = await GetRequiredSettingAsync("Integrations:Meetup:ClientId", cancellationToken);
        var redirectUri = await GetRedirectUriAsync(cancellationToken);

        return $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}"
            + $"&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task CompleteAuthorizationAsync(string code, CancellationToken cancellationToken = default)
    {
        var clientId = await GetRequiredSettingAsync("Integrations:Meetup:ClientId", cancellationToken);
        var clientSecret = await GetRequiredSettingAsync("Integrations:Meetup:ClientSecret", cancellationToken);
        var redirectUri = await GetRedirectUriAsync(cancellationToken);
        using var response = await _httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code"] = code
            }),
            cancellationToken);

        var token = await ReadTokenResponseAsync(response, cancellationToken);
        await SaveTokenAsync(token, existingRefreshToken: null, cancellationToken);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var encryptedTokenSet = await _settingsService.GetSettingAsync(TokenSettingKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(encryptedTokenSet))
        {
            return await _settingsService.GetSettingAsync(LegacyTokenSettingKey, cancellationToken)
                ?? _configuration[LegacyTokenSettingKey];
        }

        OAuthTokenSet tokenSet;
        try
        {
            tokenSet = JsonSerializer.Deserialize<OAuthTokenSet>(_tokenProtector.Unprotect(encryptedTokenSet))
                ?? throw new InvalidOperationException("Stored Meetup OAuth token data is invalid.");
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Stored Meetup OAuth credentials could not be decrypted.");
            throw new InvalidOperationException("Meetup authorization needs to be connected again.", ex);
        }

        if (tokenSet.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return tokenSet.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(tokenSet.RefreshToken))
        {
            throw new InvalidOperationException("Meetup authorization has expired. Reconnect the Meetup account in Settings.");
        }

        var clientId = await GetRequiredSettingAsync("Integrations:Meetup:ClientId", cancellationToken);
        var clientSecret = await GetRequiredSettingAsync("Integrations:Meetup:ClientSecret", cancellationToken);
        using var response = await _httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = tokenSet.RefreshToken
            }),
            cancellationToken);

        var refreshedToken = await ReadTokenResponseAsync(response, cancellationToken);
        await SaveTokenAsync(refreshedToken, tokenSet.RefreshToken, cancellationToken);
        return refreshedToken.AccessToken;
    }

    private async Task<string> GetRequiredSettingAsync(string key, CancellationToken cancellationToken)
    {
        var value = await _settingsService.GetSettingAsync(key, cancellationToken) ?? _configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Meetup integration setting '{key}' is required.");
    }

    private async Task<string> GetRedirectUriAsync(CancellationToken cancellationToken) =>
        await _settingsService.GetSettingAsync("Integrations:Meetup:RedirectUri", cancellationToken)
        ?? _configuration["Integrations:Meetup:RedirectUri"]
        ?? "http://localhost:5107/integrations/meetup/callback";

    private async Task<OAuthTokenSet> ReadTokenResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Meetup OAuth token request failed with HTTP status {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException($"Meetup OAuth token request failed (HTTP {(int)response.StatusCode}).");
        }

        var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: cancellationToken);
        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Meetup OAuth returned an empty access token.");
        }

        return new OAuthTokenSet(
            token.AccessToken,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60)));
    }

    private async Task SaveTokenAsync(
        OAuthTokenSet token,
        string? existingRefreshToken,
        CancellationToken cancellationToken)
    {
        var tokenSet = token with { RefreshToken = token.RefreshToken ?? existingRefreshToken };
        var protectedToken = _tokenProtector.Protect(JsonSerializer.Serialize(tokenSet));
        await _settingsService.SaveSettingsAsync(
            new Dictionary<string, string?> { [TokenSettingKey] = protectedToken },
            cancellationToken);
    }

    private sealed record OAuthTokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc);

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
