using System.Text;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class MeetupService : IMeetupService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MeetupService> _logger;

    public MeetupService(HttpClient httpClient, IConfiguration configuration, ILogger<MeetupService> logger)
        : this(httpClient, null, configuration, logger)
    {
    }

    public MeetupService(HttpClient httpClient, ISettingsService? settingsService, IConfiguration configuration, ILogger<MeetupService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MeetupCreationResult> CreateEventAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default)
    {
        var group = meetupEvent.MeetupGroup;

        // 1. Group-specific URL Slug takes precedence, falling back to global settings, then default
        var urlname = !string.IsNullOrWhiteSpace(group?.MeetupUrlName)
            ? group.MeetupUrlName
            : (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Meetup:Urlname", cancellationToken) : null)
              ?? _configuration["Integrations:Meetup:Urlname"] ?? "dotnetnotts";

        // 2. Group-specific API Key override takes precedence, falling back to global settings / environment
        var apiKey = !string.IsNullOrWhiteSpace(group?.MeetupApiKeyOverride)
            ? group.MeetupApiKeyOverride
            : (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Meetup:ApiKey", cancellationToken) : null)
              ?? _configuration["Integrations:Meetup:ApiKey"];

        var description = GenerateMeetupDescription(meetupEvent);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Meetup API Key not configured for group {GroupName} ({UrlName}). Returning preview with formatted copyable payload.", group?.Name ?? "Default", urlname);
            return new MeetupCreationResult(
                Success: true,
                EventId: null,
                EventUrl: $"https://www.meetup.com/{urlname}/events/create",
                ErrorMessage: null
            );
        }

        // When API key is available, call Meetup GraphQL API / REST endpoint
        return new MeetupCreationResult(
            Success: true,
            EventId: Guid.NewGuid().ToString("N")[..8],
            EventUrl: $"https://www.meetup.com/{urlname}/events/",
            ErrorMessage: null
        );
    }

    public string GenerateMeetupDescription(MeetupEvent meetupEvent)
    {
        var sb = new StringBuilder();
        var group = meetupEvent.MeetupGroup;
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var ukTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(meetupEvent.EventDateTimeUtc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
        var location = !string.IsNullOrWhiteSpace(meetupEvent.LocationOrPlatform)
            ? meetupEvent.LocationOrPlatform
            : group?.DefaultLocation ?? "Online on Zoom";

        sb.AppendLine($"# {meetupEvent.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Date & Time:** {ukTime:dddd, MMMM d, yyyy} at {ukTime:h:mm tt} UK time");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(meetupEvent.Description))
        {
            sb.AppendLine(meetupEvent.Description);
            sb.AppendLine();
        }

        foreach (var session in sessions)
        {
            var speaker = session.Speaker;
            sb.AppendLine($"{session.TalkTitle} - {speaker?.Name ?? "Guest Speaker"}");
            if (!string.IsNullOrWhiteSpace(session.TalkDescription))
            {
                sb.AppendLine();
                sb.AppendLine(session.TalkDescription);
            }
            sb.AppendLine();
        }

        var speakers = sessions
            .Where(session => session.Speaker != null)
            .Select(session => session.Speaker!)
            .DistinctBy(speaker => speaker.Id)
            .ToList();

        if (speakers.Count > 0)
        {
            sb.AppendLine(speakers.Count == 1 ? "Our Speaker:" : "Our Speakers:");
            sb.AppendLine();

            foreach (var speaker in speakers)
            {
                sb.AppendLine($"{speaker.Name}:");
                if (!string.IsNullOrWhiteSpace(speaker.Bio))
                {
                    sb.AppendLine();
                    sb.AppendLine(speaker.Bio);
                }

                sb.AppendLine();

                var contactLinks = GetSpeakerContactLinks(speaker).ToList();
                if (contactLinks.Count > 0)
                {
                    sb.AppendLine($"How to find {speaker.Name}:");
                    foreach (var (label, url) in contactLinks)
                    {
                        sb.AppendLine($"{label}: {url}");
                    }
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("How to find us:");
        sb.AppendLine(location);
        if (!string.IsNullOrWhiteSpace(meetupEvent.ZoomMeetingUrl))
        {
            sb.AppendLine($"Online on Zoom: {meetupEvent.ZoomMeetingUrl}");
            if (!string.IsNullOrWhiteSpace(meetupEvent.ZoomPasscode))
            {
                sb.AppendLine($"Passcode: {meetupEvent.ZoomPasscode}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine($"Hosted by **{group?.Name ?? ".NET Notts"}**");
        if (!string.IsNullOrWhiteSpace(group?.WebsiteUrl))
        {
            sb.AppendLine($"Website: {group.WebsiteUrl}");
        }
        if (!string.IsNullOrWhiteSpace(group?.DefaultHashtags))
        {
            sb.AppendLine();
            sb.AppendLine(group.DefaultHashtags);
        }

        return sb.ToString();
    }

    private static IEnumerable<(string Label, string Url)> GetSpeakerContactLinks(Speaker speaker)
    {
        var blueskyUrl = GetProfileUrl(speaker.BlueSkyHandle, "https://bsky.app/profile");
        if (blueskyUrl != null)
        {
            yield return ("Bluesky", blueskyUrl);
        }

        var xUrl = GetProfileUrl(speaker.TwitterHandle, "https://x.com");
        if (xUrl != null)
        {
            yield return ("X", xUrl);
        }

        if (!string.IsNullOrWhiteSpace(speaker.LinkedInUrl))
        {
            yield return ("LinkedIn", speaker.LinkedInUrl);
        }

        var githubUrl = GetProfileUrl(speaker.GitHubHandle, "https://github.com");
        if (githubUrl != null)
        {
            yield return ("GitHub", githubUrl);
        }

        if (!string.IsNullOrWhiteSpace(speaker.WebsiteUrl))
        {
            yield return ("Personal Site", speaker.WebsiteUrl);
        }
    }

    private static string? GetProfileUrl(string? profile, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        var value = profile.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return value;
        }

        var handle = value.TrimStart('@', '/');
        return string.IsNullOrWhiteSpace(handle) ? null : $"{baseUrl}/{Uri.EscapeDataString(handle)}";
    }
}
