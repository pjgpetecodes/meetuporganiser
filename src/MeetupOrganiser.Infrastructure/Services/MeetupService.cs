using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Xml;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class MeetupService : IMeetupService
{
    private const string GraphQlEndpoint = "https://api.meetup.com/gql-ext";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MeetupService> _logger;
    private readonly IMeetupOAuthService? _meetupOAuthService;

    public MeetupService(HttpClient httpClient, IConfiguration configuration, ILogger<MeetupService> logger)
        : this(httpClient, null, configuration, logger, null)
    {
    }

    public MeetupService(HttpClient httpClient, ISettingsService? settingsService, IConfiguration configuration, ILogger<MeetupService> logger)
        : this(httpClient, settingsService, configuration, logger, null)
    {
    }

    public MeetupService(
        HttpClient httpClient,
        ISettingsService? settingsService,
        IConfiguration configuration,
        ILogger<MeetupService> logger,
        IMeetupOAuthService? meetupOAuthService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _configuration = configuration;
        _logger = logger;
        _meetupOAuthService = meetupOAuthService;
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
        var accessToken = !string.IsNullOrWhiteSpace(group?.MeetupApiKeyOverride)
            ? group.MeetupApiKeyOverride
            : (_settingsService != null ? await _settingsService.GetSettingAsync("Integrations:Meetup:ApiKey", cancellationToken) : null)
              ?? _configuration["Integrations:Meetup:ApiKey"];

        if (_meetupOAuthService != null)
        {
            accessToken = await _meetupOAuthService.GetAccessTokenAsync(cancellationToken) ?? accessToken;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogInformation("Meetup API Key not configured for group {GroupName} ({UrlName}). Returning preview with formatted copyable payload.", group?.Name ?? "Default", urlname);
            return new MeetupCreationResult(
                Success: true,
                EventId: null,
                EventUrl: $"https://www.meetup.com/{urlname}/events/create",
                ErrorMessage: null
            );
        }

        var mutation = """
            mutation CreateMeetupEvent($input: CreateEventInput!) {
              createEvent(input: $input) {
                event { id eventUrl }
                errors { message code field }
              }
            }
            """;
        var variables = new
        {
            input = new
            {
                groupUrlname = urlname,
                title = meetupEvent.Title,
                description = GenerateMeetupDescription(
                    meetupEvent,
                    _settingsService != null ? await _settingsService.GetSettingAsync(SpeakerBio.SettingKey, cancellationToken) : null,
                    (_settingsService != null ? await _settingsService.GetSettingAsync(MeetupDescriptionTemplate.SettingKey, cancellationToken) : null)
                        ?? MeetupDescriptionTemplate.Default),
                startDateTime = DateTime.SpecifyKind(meetupEvent.EventDateTimeUtc, DateTimeKind.Utc).ToString("O"),
                duration = XmlConvert.ToString(TimeSpan.FromMinutes(meetupEvent.DurationMinutes)),
                publishStatus = "PUBLISHED",
                howToFindUs = meetupEvent.LocationOrPlatform
            }
        };

        try
        {
            using var document = await SendGraphQlAsync(mutation, variables, accessToken, cancellationToken);
            var root = document.RootElement.GetProperty("data").GetProperty("createEvent");
            var mutationErrors = GetErrorMessages(root);
            if (mutationErrors.Count > 0)
            {
                return new MeetupCreationResult(false, null, null, string.Join("; ", mutationErrors));
            }

            if (!root.TryGetProperty("event", out var createdEvent)
                || !createdEvent.TryGetProperty("id", out var eventIdElement)
                || string.IsNullOrWhiteSpace(eventIdElement.GetString()))
            {
                return new MeetupCreationResult(false, null, null, "Meetup did not return a created event ID.");
            }

            var eventId = eventIdElement.GetString()!;
            var eventUrl = createdEvent.TryGetProperty("eventUrl", out var urlElement)
                ? urlElement.GetString()
                : null;
            eventUrl ??= $"https://www.meetup.com/{urlname}/events/{eventId}/";
            return new MeetupCreationResult(true, eventId, eventUrl, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "Meetup event creation failed for event {EventTitle}.", meetupEvent.Title);
            return new MeetupCreationResult(false, null, null, ex.Message);
        }
    }

    public async Task<MeetupEventImportResult> ImportPastEventsAsync(
        string urlname,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlname))
        {
            return new MeetupEventImportResult(false, [], "A Meetup group URL name is required.");
        }

        var accessToken = _meetupOAuthService != null
            ? await _meetupOAuthService.GetAccessTokenAsync(cancellationToken)
            : null;
        accessToken ??= _settingsService != null
            ? await _settingsService.GetSettingAsync("Integrations:Meetup:ApiKey", cancellationToken)
            : _configuration["Integrations:Meetup:ApiKey"];
        accessToken ??= _configuration["Integrations:Meetup:ApiKey"];
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new MeetupEventImportResult(false, [], "Connect a Meetup OAuth account before importing past events.");
        }

        const string query = """
            query PastMeetupEvents($urlname: ID!, $first: Int!, $after: String, $status: EventStatus!) {
              groupByUrlname(urlname: $urlname) {
                events(first: $first, after: $after, status: $status) {
                  edges {
                    node {
                      id
                      title
                      description
                      dateTime
                      duration
                      eventUrl
                      howToFindUs
                      speakerDetails { name description }
                      venue { name }
                    }
                  }
                  pageInfo { endCursor hasNextPage }
                }
              }
            }
            """;

        try
        {
            var importedEvents = new List<MeetupEventImportDto>();
            string? cursor = null;
            do
            {
                var variables = new { urlname, first = 50, after = cursor, status = "PAST" };
                using var document = await SendGraphQlAsync(query, variables, accessToken, cancellationToken);
                var group = document.RootElement.GetProperty("data").GetProperty("groupByUrlname");
                if (group.ValueKind == JsonValueKind.Null)
                {
                    return new MeetupEventImportResult(false, [], $"Meetup group '{urlname}' was not found or is not accessible.");
                }

                var events = group.GetProperty("events");
                foreach (var edge in events.GetProperty("edges").EnumerateArray())
                {
                    var node = edge.GetProperty("node");
                    if (!DateTimeOffset.TryParse(GetString(node, "dateTime"), out var eventDateTime))
                    {
                        _logger.LogWarning("Skipping Meetup event {EventId}: invalid event date.", GetString(node, "id"));
                        continue;
                    }

                    var eventTitle = GetString(node, "title") ?? "Untitled Meetup event";
                    var description = GetString(node, "description");
                    var speakers = ReadSpeakers(node, eventTitle, description);
                    var duration = ParseDurationMinutes(GetString(node, "duration"));
                    var venue = node.TryGetProperty("venue", out var venueNode) && venueNode.ValueKind == JsonValueKind.Object
                        ? GetString(venueNode, "name")
                        : null;
                    var howToFindUs = GetString(node, "howToFindUs");
                    var location = string.Join(", ", new[] { venue, howToFindUs }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase));

                    importedEvents.Add(new MeetupEventImportDto(
                        GetString(node, "id") ?? throw new JsonException("Meetup event response did not include an ID."),
                        eventTitle,
                        LimitLength(description, 8000),
                        eventDateTime.UtcDateTime,
                        duration,
                        GetString(node, "eventUrl"),
                        LimitLength(location, 200),
                        speakers));
                }

                var pageInfo = events.GetProperty("pageInfo");
                var hasNextPage = pageInfo.GetProperty("hasNextPage").GetBoolean();
                cursor = GetString(pageInfo, "endCursor");
                if (hasNextPage && string.IsNullOrWhiteSpace(cursor))
                {
                    throw new JsonException("Meetup event pagination indicated another page but returned no cursor.");
                }

                if (!hasNextPage)
                {
                    break;
                }
            } while (true);

            return new MeetupEventImportResult(true, importedEvents, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            _logger.LogError(ex, "Importing past Meetup events failed for group {GroupUrlname}.", urlname);
            return new MeetupEventImportResult(false, [], ex.Message);
        }
    }

    public string GenerateMeetupDescription(MeetupEvent meetupEvent, string? bioPreference = null, string? template = null)
    {
        // A description populated from the template in the event editor is used as-is.
        if (meetupEvent.Description?.TrimStart().StartsWith("# ", StringComparison.Ordinal) == true)
        {
            return meetupEvent.Description;
        }

        if (!string.IsNullOrWhiteSpace(template))
        {
            return RenderTemplate(meetupEvent, bioPreference, template);
        }

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
            var talkDescription = SpeakerBio.ResolveTalkDescription(session.TalkDescription, speaker, bioPreference);
            if (!string.IsNullOrWhiteSpace(talkDescription))
            {
                sb.AppendLine();
                sb.AppendLine(talkDescription);
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
                var bio = SpeakerBio.Resolve(speaker, bioPreference);
                if (!string.IsNullOrWhiteSpace(bio))
                {
                    sb.AppendLine();
                    sb.AppendLine(bio);
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

    private static string RenderTemplate(MeetupEvent meetupEvent, string? bioPreference, string template)
    {
        var group = meetupEvent.MeetupGroup;
        var sessions = meetupEvent.Sessions.OrderBy(s => s.OrderIndex).ToList();
        var ukTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(meetupEvent.EventDateTimeUtc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
        var speakers = sessions
            .Where(session => session.Speaker != null)
            .Select(session => session.Speaker!)
            .DistinctBy(speaker => speaker.Id)
            .ToList();
        var first = sessions.FirstOrDefault();

        var talks = new StringBuilder();
        var talkDescriptions = new StringBuilder();
        foreach (var session in sessions)
        {
            var description = SpeakerBio.ResolveTalkDescription(session.TalkDescription, session.Speaker, bioPreference);
            talks.AppendLine($"{session.TalkTitle} - {session.Speaker?.Name ?? "Guest Speaker"}").AppendLine();
            if (!string.IsNullOrWhiteSpace(description))
            {
                talks.AppendLine(description).AppendLine();
                talkDescriptions.AppendLine(description).AppendLine();
            }
        }

        var speakerSection = new StringBuilder();
        if (speakers.Count > 0)
        {
            speakerSection.AppendLine(speakers.Count == 1 ? "Our Speaker:" : "Our Speakers:").AppendLine();
            foreach (var speaker in speakers)
            {
                speakerSection.AppendLine($"{speaker.Name}:").AppendLine();
                var bio = SpeakerBio.Resolve(speaker, bioPreference);
                if (!string.IsNullOrWhiteSpace(bio))
                {
                    speakerSection.AppendLine(bio).AppendLine();
                }

                var links = GetSpeakerContactLinks(speaker).ToList();
                if (links.Count > 0)
                {
                    var firstName = speaker.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? speaker.Name;
                    speakerSection.AppendLine($"How to find {firstName}:").AppendLine();
                    foreach (var (label, url) in links)
                    {
                        speakerSection.AppendLine($"{label}: {url}").AppendLine();
                    }
                }
            }
        }

        var zoomSection = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(meetupEvent.ZoomMeetingUrl))
        {
            zoomSection.AppendLine($"Online on Zoom: {meetupEvent.ZoomMeetingUrl}");
            if (!string.IsNullOrWhiteSpace(meetupEvent.ZoomPasscode))
            {
                zoomSection.AppendLine($"Passcode: {meetupEvent.ZoomPasscode}");
            }
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EventTitle"] = meetupEvent.Title,
            ["EventDate"] = ukTime.ToString("dddd, MMMM d, yyyy"),
            ["EventTime"] = ukTime.ToString("h:mm tt"),
            ["EventDescription"] = meetupEvent.Description?.Trim() ?? string.Empty,
            ["TalkTitle"] = first?.TalkTitle ?? string.Empty,
            ["SpeakerName"] = string.Join(" and ", speakers.Select(s => s.Name)),
            ["Talks"] = talks.ToString().Trim(),
            ["TalkDescriptions"] = talkDescriptions.ToString().Trim(),
            ["SpeakerSection"] = speakerSection.ToString().Trim(),
            ["Location"] = !string.IsNullOrWhiteSpace(meetupEvent.LocationOrPlatform)
                ? meetupEvent.LocationOrPlatform
                : group?.DefaultLocation ?? "Online on Zoom",
            ["ZoomUrl"] = meetupEvent.ZoomMeetingUrl ?? string.Empty,
            ["ZoomPasscode"] = meetupEvent.ZoomPasscode ?? string.Empty,
            ["ZoomSection"] = zoomSection.ToString().Trim(),
            ["GroupName"] = group?.Name ?? ".NET Notts",
            ["WebsiteUrl"] = group?.WebsiteUrl ?? string.Empty,
            ["Hashtags"] = group?.DefaultHashtags ?? string.Empty
        };

        return MeetupDescriptionTemplate.Render(template, values);
    }

    private async Task<JsonDocument> SendGraphQlAsync(
        string query,
        object variables,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
        {
            Content = JsonContent.Create(new { query, variables })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Meetup API returned HTTP {(int)response.StatusCode}.");
        }

        var document = JsonDocument.Parse(responseBody);
        var errors = GetErrorMessages(document.RootElement);
        if (errors.Count > 0)
        {
            document.Dispose();
            throw new InvalidOperationException($"Meetup API request failed: {string.Join("; ", errors)}");
        }

        if (!document.RootElement.TryGetProperty("data", out _))
        {
            document.Dispose();
            throw new JsonException("Meetup API response did not contain data.");
        }

        return document;
    }

    private static List<string> GetErrorMessages(JsonElement element)
    {
        if (!element.TryGetProperty("errors", out var errorsElement)
            || errorsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return errorsElement.EnumerateArray()
            .Select(error => GetString(error, "message") ?? "Unknown Meetup API error.")
            .ToList();
    }

    private static List<MeetupEventImportSpeaker> ReadSpeakers(
        JsonElement eventNode,
        string eventTitle,
        string? description)
    {
        if (eventNode.TryGetProperty("speakerDetails", out var speakerDetails)
            && speakerDetails.ValueKind == JsonValueKind.Object)
        {
            var name = GetString(speakerDetails, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return
                [
                    new MeetupEventImportSpeaker(
                        name,
                        LimitLength(GetString(speakerDetails, "description"), 4000),
                        eventTitle)
                ];
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        var speakerSection = Regex.Match(
            description,
            @"(?is)\bOur Speakers?:\s*(?<section>.*?)(?=\bHow to find\b|$)");
        if (speakerSection.Success)
        {
            var section = speakerSection.Groups["section"].Value;
            var names = Regex.Matches(section, @"(?m)^\s*(?<name>[^:\r\n]{2,100}):\s*$");
            if (names.Count > 0)
            {
                var imported = new List<MeetupEventImportSpeaker>();
                for (var index = 0; index < names.Count; index++)
                {
                    var nameMatch = names[index];
                    var start = nameMatch.Index + nameMatch.Length;
                    var end = index + 1 < names.Count ? names[index + 1].Index : section.Length;
                    var bio = section[start..end].Trim();
                    imported.Add(new MeetupEventImportSpeaker(
                        nameMatch.Groups["name"].Value.Trim(),
                        LimitLength(bio, 4000),
                        eventTitle));
                }
                return imported;
            }
        }

        var inferred = InferSpeakerFromTitle(eventTitle, description);
        return inferred == null ? [] : [inferred];
    }

    private static MeetupEventImportSpeaker? InferSpeakerFromTitle(string title, string description)
    {
        var match = Regex.Match(title, @"^(?<left>.+?)\s+[-–—]\s+(?<right>.+)$");
        string? speakerName = null;
        string? talkTitle = null;
        if (match.Success)
        {
            var left = match.Groups["left"].Value.Trim();
            var right = match.Groups["right"].Value.Trim();
            var leftMentioned = description.Contains(left, StringComparison.OrdinalIgnoreCase);
            var rightMentioned = description.Contains(right, StringComparison.OrdinalIgnoreCase);
            if (leftMentioned && !rightMentioned)
            {
                speakerName = left;
                talkTitle = right;
            }
            else if (rightMentioned && !leftMentioned)
            {
                speakerName = right;
                talkTitle = left;
            }
        }
        else
        {
            match = Regex.Match(title, @"^(?<name>[A-Z][\p{L}'’. -]{1,80}?)\s+on\s+(?<talk>.+)$");
            if (match.Success)
            {
                var possibleName = match.Groups["name"].Value.Trim();
                if (description.Contains(possibleName, StringComparison.OrdinalIgnoreCase))
                {
                    speakerName = possibleName;
                    talkTitle = match.Groups["talk"].Value.Trim();
                }
            }
        }

        if (speakerName == null)
        {
            return null;
        }

        var bio = description.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None)
            .FirstOrDefault(paragraph => paragraph.Contains(speakerName, StringComparison.OrdinalIgnoreCase));
        return new MeetupEventImportSpeaker(speakerName, LimitLength(bio, 4000), talkTitle);
    }

    private static int ParseDurationMinutes(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return 90;
        }

        try
        {
            var minutes = (int)Math.Ceiling(XmlConvert.ToTimeSpan(duration).TotalMinutes);
            return minutes > 0 ? minutes : 90;
        }
        catch (FormatException)
        {
            return 90;
        }
    }

    private static string? LimitLength(string? value, int maximumLength) =>
        value is { Length: > 0 } && value.Length > maximumLength
            ? value[..maximumLength]
            : value;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

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
