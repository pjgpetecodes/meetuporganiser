using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class SessionizeProfileService : ISessionizeProfileService
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<SessionizeProfileService> _logger;

    public SessionizeProfileService(HttpClient httpClient, ILogger<SessionizeProfileService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(SessionizeProfileUrl.BaseUrl);
        }

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MeetupOrganiser/1.0 (+speaker profile lookup)");
        }
    }

    public async Task<List<SessionizeProfileCandidate>> SearchAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];

        var html = await FetchAsync($"/speakers-directory?q={Uri.EscapeDataString(name.Trim())}", cancellationToken);
        return html == null ? [] : SessionizeProfileParser.ParseSearchResults(html).Take(10).ToList();
    }

    public async Task<SessionizeProfile?> GetProfileAsync(string urlOrSlug, CancellationToken cancellationToken = default)
    {
        var slug = SessionizeProfileUrl.TryGetSlug(urlOrSlug);
        if (slug == null) return null;

        var html = await FetchAsync($"/{slug}", cancellationToken);
        return html == null ? null : SessionizeProfileParser.ParseProfile(html, slug);
    }

    private async Task<string?> FetchAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await _httpClient.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var finalHost = response.RequestMessage?.RequestUri?.Host;
        if (!string.Equals(finalHost, "sessionize.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Sessionize lookup redirected to unexpected host {Host}.", finalHost);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxResponseBytes)
            {
                _logger.LogWarning("Sessionize response exceeded {Max} bytes.", MaxResponseBytes);
                return null;
            }
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
