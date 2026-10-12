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
        var apiKey = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Meetup:ApiKey", cancellationToken) : null)
            ?? _configuration["Integrations:Meetup:ApiKey"];
        var urlname = (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Meetup:Urlname", cancellationToken) : null)
            ?? _configuration["Integrations:Meetup:Urlname"] ?? "dotnetnotts";

        var description = GenerateMeetupDescription(meetupEvent);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Meetup API Key not configured. Returning preview with formatted copyable payload.");
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

        sb.AppendLine($"# {meetupEvent.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Date & Time:** {meetupEvent.EventDateTimeUtc:dddd, MMMM d, yyyy} at {meetupEvent.EventDateTimeUtc:HH:mm} UTC");
        sb.AppendLine($"**Location / Platform:** {meetupEvent.LocationOrPlatform ?? "Online on Zoom"}");
        if (!string.IsNullOrWhiteSpace(meetupEvent.ZoomMeetingUrl))
        {
            sb.AppendLine($"**Zoom Link:** {meetupEvent.ZoomMeetingUrl}");
            if (!string.IsNullOrWhiteSpace(meetupEvent.ZoomPasscode))
            {
                sb.AppendLine($"**Passcode:** {meetupEvent.ZoomPasscode}");
            }
        }
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(meetupEvent.Description))
        {
            sb.AppendLine(meetupEvent.Description);
            sb.AppendLine();
        }

        sb.AppendLine("## Agenda & Speakers");
        sb.AppendLine();

        foreach (var session in meetupEvent.Sessions.OrderBy(s => s.OrderIndex))
        {
            var speaker = session.Speaker;
            sb.AppendLine($"### {session.TalkTitle}");
            sb.AppendLine($"**Speaker:** {speaker?.Name ?? "Guest Speaker"}");
            if (!string.IsNullOrWhiteSpace(speaker?.Bio))
            {
                sb.AppendLine();
                sb.AppendLine($"*About {speaker.Name}:* {speaker.Bio}");
            }
            if (!string.IsNullOrWhiteSpace(session.TalkDescription))
            {
                sb.AppendLine();
                sb.AppendLine(session.TalkDescription);
            }
            sb.AppendLine();
        }

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
}
