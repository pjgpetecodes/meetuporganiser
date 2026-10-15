using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
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
                    !string.IsNullOrWhiteSpace(group?.BioPreference)
                        ? group.BioPreference
                        : _settingsService != null ? await _settingsService.GetSettingAsync(SpeakerBio.SettingKey, cancellationToken) : null,
                    (!string.IsNullOrWhiteSpace(group?.MeetupDescriptionTemplate) ? group.MeetupDescriptionTemplate : null)
                        ?? (_settingsService != null ? await _settingsService.GetSettingAsync(MeetupDescriptionTemplate.SettingKey, cancellationToken) : null)
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
            return await ImportPastEventsFromHtmlAsync(urlname, cancellationToken);
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
                    return await ImportPastEventsFromHtmlAsync(
                        urlname,
                        cancellationToken,
                        $"Meetup API could not access group '{urlname}'.");
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

            if (importedEvents.Count > 0)
            {
                return new MeetupEventImportResult(true, importedEvents, null);
            }

            return await ImportPastEventsFromHtmlAsync(
                urlname,
                cancellationToken,
                "Meetup API returned no past events.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            _logger.LogError(ex, "Importing past Meetup events failed for group {GroupUrlname}.", urlname);
            return await ImportPastEventsFromHtmlAsync(urlname, cancellationToken, ex.Message);
        }
    }

    public async Task<int?> GetAttendeeCountAsync(string eventUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(eventUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !IsMeetupHost(uri.Host)
            || !uri.AbsolutePath.Contains("/events/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid Meetup event URL is required.", nameof(eventUrl));
        }

        var html = await FetchMeetupHtmlAsync(uri, cancellationToken);
        return ParseAttendeeCount(html);
    }

    private async Task<MeetupEventImportResult> ImportPastEventsFromHtmlAsync(
        string urlname,
        CancellationToken cancellationToken,
        string? apiError = null)
    {
        if (!Regex.IsMatch(urlname, @"\A[A-Za-z0-9_-]{1,100}\z"))
        {
            return new MeetupEventImportResult(false, [],
                "The Meetup group URL name is invalid." + FormatApiError(apiError));
        }

        try
        {
            var eventById = new Dictionary<string, MeetupEventImportDto>(StringComparer.OrdinalIgnoreCase);
            var eventLinks = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            var listingErrors = new List<string>();
            foreach (var eventType in new[] { "past", "upcoming" })
            {
                var groupUrl = new Uri(
                    $"https://www.meetup.com/{Uri.EscapeDataString(urlname)}/events/?type={eventType}");
                try
                {
                    var groupHtml = await FetchMeetupHtmlAsync(groupUrl, cancellationToken);
                    foreach (var importedEvent in ParsePastEventsFromListing(groupHtml))
                    {
                        eventById.TryAdd(importedEvent.MeetupEventId, importedEvent);
                    }

                    foreach (var (eventId, eventUrl) in ParsePastEventLinks(groupHtml, urlname))
                    {
                        eventLinks.TryAdd(eventId, eventUrl);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                {
                    _logger.LogWarning(ex, "Could not read Meetup {EventType} event listing for group {GroupUrlname}.", eventType, urlname);
                    listingErrors.Add(ex.Message);
                }
            }

            if (eventById.Count > 0)
            {
                return new MeetupEventImportResult(
                    true,
                    eventById.Values.OrderByDescending(item => item.EventDateTimeUtc).ToList(),
                    null);
            }

            if (eventLinks.Count == 0)
            {
                var reason = listingErrors.Count > 0
                    ? $" Meetup listing errors: {string.Join("; ", listingErrors.Distinct())}."
                    : string.Empty;
                return new MeetupEventImportResult(false, [],
                    "Meetup's public pages did not expose any past event data or links." + reason + FormatApiError(apiError));
            }

            var importedEvents = new Dictionary<string, MeetupEventImportDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var (eventId, eventUrl) in eventLinks.Take(100))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var eventHtml = await FetchMeetupHtmlAsync(eventUrl, cancellationToken);
                    var importedEvent = ParsePastEvent(eventHtml, eventId, eventUrl);
                    if (importedEvent == null)
                    {
                        _logger.LogWarning("Skipping scraped Meetup event {EventId}: missing event date.", eventId);
                        continue;
                    }

                    if (importedEvent.EventDateTimeUtc < DateTime.UtcNow)
                    {
                        importedEvents.TryAdd(importedEvent.MeetupEventId, importedEvent);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                {
                    _logger.LogWarning(ex, "Could not scrape Meetup event {EventId}.", eventId);
                }
            }

            if (importedEvents.Count == 0)
            {
                return new MeetupEventImportResult(false, [],
                    "Meetup's public pages did not contain any readable past events." + FormatApiError(apiError));
            }

            _logger.LogInformation(
                "Scraped {EventCount} past Meetup events for group {GroupUrlname} after API import was unavailable.",
                importedEvents.Count,
                urlname);
            return new MeetupEventImportResult(
                true,
                importedEvents.Values.OrderByDescending(item => item.EventDateTimeUtc).ToList(),
                null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Scraping past Meetup events failed for group {GroupUrlname}.", urlname);
            return new MeetupEventImportResult(false, [],
                $"Meetup's public event pages could not be read: {ex.Message}" + FormatApiError(apiError));
        }
    }

    private static List<MeetupEventImportDto> ParsePastEventsFromListing(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var nextData = document.QuerySelector("script#__NEXT_DATA__")?.TextContent;
        if (string.IsNullOrWhiteSpace(nextData)) return [];

        using var json = JsonDocument.Parse(nextData);
        if (!json.RootElement.TryGetProperty("props", out var props)
            || !props.TryGetProperty("pageProps", out var pageProps)
            || !pageProps.TryGetProperty("__APOLLO_STATE__", out var apolloState)
            || apolloState.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var events = new List<MeetupEventImportDto>();
        foreach (var entity in apolloState.EnumerateObject())
        {
            var eventData = entity.Value;
            if (GetString(eventData, "__typename") != "Event") continue;
            if (TryParsePastMeetupEvent(eventData, apolloState, out var importedEvent))
            {
                events.Add(importedEvent);
            }
        }

        return events;
    }

    private static bool TryParsePastMeetupEvent(
        JsonElement eventData,
        JsonElement apolloState,
        out MeetupEventImportDto importedEvent)
    {
        importedEvent = default!;
        if (!DateTimeOffset.TryParse(GetString(eventData, "dateTime"), out var startDate)
            || startDate.UtcDateTime >= DateTime.UtcNow)
        {
            return false;
        }

        var id = GetString(eventData, "id");
        var eventUrl = GetString(eventData, "eventUrl");
        var title = GetString(eventData, "title");
        if (string.IsNullOrWhiteSpace(id)
            || id.Length > 100
            || string.IsNullOrWhiteSpace(title)
            || !Uri.TryCreate(eventUrl, UriKind.Absolute, out var parsedEventUrl)
            || !IsMeetupHost(parsedEventUrl.Host))
        {
            return false;
        }

        var description = LimitLength(GetString(eventData, "description"), 8000);
        var duration = ParseDurationMinutes(null);
        if (DateTimeOffset.TryParse(GetString(eventData, "endTime"), out var endDate))
        {
            var durationMinutes = (int)Math.Ceiling((endDate - startDate).TotalMinutes);
            if (durationMinutes > 0) duration = durationMinutes;
        }

        var location = GetMeetupVenueName(eventData, apolloState);
        var structuredSpeakers = ReadSpeakers(eventData, title, description);
        importedEvent = new MeetupEventImportDto(
            id,
            LimitLength(title.Trim(), 250) ?? "Untitled Meetup event",
            description,
            startDate.UtcDateTime,
            duration,
            parsedEventUrl.AbsoluteUri,
            LimitLength(location, 200),
            structuredSpeakers,
            GetMeetupImageUrl(eventData, apolloState));
        return true;
    }

    private static string? GetMeetupImageUrl(JsonElement eventData, JsonElement apolloState)
    {
        foreach (var property in new[] { "featuredEventPhoto", "displayPhoto" })
        {
            if (!eventData.TryGetProperty(property, out var photo)) continue;
            if (photo.ValueKind == JsonValueKind.Object
                && photo.TryGetProperty("__ref", out var reference)
                && reference.ValueKind == JsonValueKind.String
                && reference.GetString() is { } key
                && apolloState.TryGetProperty(key, out var referencedPhoto))
            {
                photo = referencedPhoto;
            }

            if (photo.ValueKind != JsonValueKind.Object) continue;
            var url = GetString(photo, "highResUrl") ?? GetString(photo, "baseUrl");
            if (IsSafeMeetupImageUrl(url)) return url;
        }

        return null;
    }

    private static bool IsSafeMeetupImageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && url!.Length <= 1000
        && (uri.Host.EndsWith("meetupstatic.com", StringComparison.OrdinalIgnoreCase)
            || IsMeetupHost(uri.Host));

    private static string? GetMeetupVenueName(JsonElement eventData, JsonElement apolloState)
    {
        if (!eventData.TryGetProperty("venue", out var venue)) return null;
        if (venue.ValueKind == JsonValueKind.Object
            && venue.TryGetProperty("__ref", out var reference)
            && reference.ValueKind == JsonValueKind.String
            && apolloState.TryGetProperty(reference.GetString(), out var referencedVenue))
        {
            venue = referencedVenue;
        }

        if (venue.ValueKind != JsonValueKind.Object) return null;
        var name = GetString(venue, "name");
        var address = venue.TryGetProperty("address", out var addressValue)
            ? addressValue.ValueKind == JsonValueKind.String
                ? addressValue.GetString()
                : addressValue.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", new[]
                    {
                        GetString(addressValue, "streetAddress"),
                        GetString(addressValue, "addressLocality"),
                        GetString(addressValue, "addressRegion"),
                        GetString(addressValue, "postalCode")
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : null
            : null;
        return string.Join(", ", new[] { name, address }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<string> FetchMeetupHtmlAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MeetupOrganiser/1.0 (+past event import)");
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri;
        if (finalUri == null || !IsMeetupHost(finalUri.Host))
        {
            throw new HttpRequestException("Meetup redirected to an unexpected host.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > 6 * 1024 * 1024)
            {
                throw new InvalidOperationException("Meetup's HTML response exceeded the 6 MB limit.");
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static List<(string EventId, Uri EventUrl)> ParsePastEventLinks(string html, string urlname)
    {
        var document = new HtmlParser().ParseDocument(html);
        var links = new List<(string EventId, Uri EventUrl)>();
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (!Uri.TryCreate(new Uri("https://www.meetup.com"), href, out var eventUrl)
                || !IsMeetupHost(eventUrl.Host))
            {
                continue;
            }

            var segments = eventUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3
                || !string.Equals(Uri.UnescapeDataString(segments[0]), urlname, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(segments[1], "events", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eventId = segments[2];
            if (eventId.Length is < 1 or > 100 || eventId.Any(char.IsControl))
            {
                continue;
            }

            links.Add((eventId, new Uri($"https://www.meetup.com/{Uri.EscapeDataString(urlname)}/events/{Uri.EscapeDataString(eventId)}/")));
        }

        return links.DistinctBy(link => link.EventId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static MeetupEventImportDto? ParsePastEvent(string html, string eventId, Uri eventUrl)
    {
        var document = new HtmlParser().ParseDocument(html);
        JsonElement? eventData = null;
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            if (string.IsNullOrWhiteSpace(script.TextContent)) continue;
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                eventData ??= FindJsonLdEvent(json.RootElement);
            }
            catch (JsonException)
            {
                // Meetup pages can include unrelated or incomplete structured-data scripts.
            }
        }

        var title = eventData is { } data
            ? GetJsonLdString(data, "name")
            : null;
        title ??= document.QuerySelector("h1")?.TextContent.Trim();
        title ??= document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;
        title = Regex.Replace(title, @"\s*[|–-]\s*Meetup\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();

        var dateText = eventData is { } structured
            ? GetJsonLdString(structured, "startDate")
            : null;
        dateText ??= document.QuerySelector("time[datetime]")?.GetAttribute("datetime");
        if (!DateTimeOffset.TryParse(dateText, out var startDate)) return null;

        var description = eventData is { } eventJson
            ? GetJsonLdString(eventJson, "description")
            : null;
        description ??= document.QuerySelector("meta[property='og:description']")?.GetAttribute("content");
        description ??= document.QuerySelector("meta[name='description']")?.GetAttribute("content");
        description ??= document.QuerySelector("[data-testid*='description'], [class*='description']")?.TextContent.Trim();
        description = LimitLength(description, 8000);

        var durationText = eventData is { } durationData
            ? GetJsonLdString(durationData, "duration")
            : null;
        var duration = ParseDurationMinutes(durationText);
        var location = eventData is { } locationData
            ? GetJsonLdLocation(locationData)
            : null;
        var structuredSpeakers = eventData is { } speakerData
            ? ReadJsonLdSpeakers(speakerData, title)
            : [];
        var speakers = structuredSpeakers
            .Concat(ReadSpeakers(default, title, description))
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.Name))
            .GroupBy(speaker => speaker.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(speaker => !string.IsNullOrWhiteSpace(speaker.Bio)) ?? group.First())
            .ToList();

        var imageUrl = document.QuerySelector("meta[property='og:image']")?.GetAttribute("content");
        if (!IsSafeMeetupImageUrl(imageUrl)) imageUrl = null;

        return new MeetupEventImportDto(
            eventId,
            LimitLength(title, 250) ?? "Untitled Meetup event",
            description,
            startDate.UtcDateTime,
            duration,
            eventUrl.AbsoluteUri,
            LimitLength(location, 200),
            speakers,
            imageUrl);
    }

    private static int? ParseAttendeeCount(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("[data-attendee-count], [data-rsvp-count], [data-going-count]"))
        {
            foreach (var attribute in new[] { "data-attendee-count", "data-rsvp-count", "data-going-count" })
            {
                if (int.TryParse(element.GetAttribute(attribute), out var count) && count >= 0)
                {
                    return count;
                }
            }
        }

        foreach (var script in document.QuerySelectorAll("script"))
        {
            if (string.IsNullOrWhiteSpace(script.TextContent)) continue;
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var count = FindAttendeeCount(json.RootElement);
                if (count.HasValue) return count;
            }
            catch (JsonException)
            {
                // Most scripts on Meetup pages are not JSON.
            }
        }

        var plainHtmlText = System.Net.WebUtility.HtmlDecode(Regex.Replace(html, @"<[^>]+>", " "));
        var pageText = Regex.Replace($"{document.Body?.TextContent} {plainHtmlText}", @"\s+", " ");
        foreach (var pattern in new[]
        {
            @"\b(?<count>\d[\d,]*)\s+(?:(?:people|members)\s+)?(?:are\s+)?(?:going|attending|attendees|attended)\b",
            @"\b(?:attendees|attending|attended|people going|going)\s*[:(]\s*(?<count>\d[\d,]*)\b"
        })
        {
            var match = Regex.Match(pageText, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["count"].Value.Replace(",", string.Empty), out var count))
            {
                return count;
            }
        }

        return null;
    }

    private static int? FindAttendeeCount(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var isGoingCount = property.Name.Equals("goingCount", StringComparison.OrdinalIgnoreCase);
                var isYesRsvpConnection = property.Name.StartsWith("rsvps(", StringComparison.OrdinalIgnoreCase)
                    && property.Name.Contains("YES", StringComparison.OrdinalIgnoreCase);
                if (property.Name.Equals("attendeeCount", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("attendeesCount", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("rsvpCount", StringComparison.OrdinalIgnoreCase)
                    || isGoingCount
                    || property.Name.Equals("yesRsvpCount", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("attendee_count", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("rsvp_count", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("yes_rsvp_count", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("going", StringComparison.OrdinalIgnoreCase)
                    || isYesRsvpConnection)
                {
                    var count = ParseNonNegativeInt(property.Value)
                        ?? ((isGoingCount || isYesRsvpConnection) ? FindTotalCount(property.Value) : null);
                    if (count.HasValue) return count;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var count = FindAttendeeCount(property.Value);
                if (count.HasValue) return count;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var count = FindAttendeeCount(item);
                if (count.HasValue) return count;
            }
        }

        return null;
    }

    private static int? FindTotalCount(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("totalCount", StringComparison.OrdinalIgnoreCase))
            {
                return ParseNonNegativeInt(property.Value);
            }
        }

        return null;
    }

    private static int? ParseNonNegativeInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0)
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out number)
            && number >= 0)
        {
            return number;
        }

        return null;
    }

    private static JsonElement? FindJsonLdEvent(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var type))
            {
                var isEvent = type.ValueKind == JsonValueKind.String
                    ? IsJsonLdEventType(type.GetString())
                    : type.ValueKind == JsonValueKind.Array
                      && type.EnumerateArray().Any(item =>
                          item.ValueKind == JsonValueKind.String && IsJsonLdEventType(item.GetString()));
                if (isEvent) return element.Clone();
            }

            foreach (var property in element.EnumerateObject())
            {
                var match = FindJsonLdEvent(property.Value);
                if (match != null) return match;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var match = FindJsonLdEvent(item);
                if (match != null) return match;
            }
        }

        return null;
    }

    private static bool IsJsonLdEventType(string? value) =>
        string.Equals(value, "Event", StringComparison.OrdinalIgnoreCase)
        || value?.EndsWith("/Event", StringComparison.OrdinalIgnoreCase) == true;

    private static List<MeetupEventImportSpeaker> ReadJsonLdSpeakers(JsonElement eventData, string eventTitle)
    {
        if (eventData.TryGetProperty("speakerDetails", out var speakerDetails)
            && speakerDetails.ValueKind == JsonValueKind.Object
            && !string.IsNullOrWhiteSpace(GetJsonLdString(speakerDetails, "name")))
        {
            return [new MeetupEventImportSpeaker(
                LimitLength(GetJsonLdString(speakerDetails, "name")!.Trim(), 200) ?? string.Empty,
                LimitLength(GetJsonLdString(speakerDetails, "description"), 4000),
                eventTitle)];
        }

        if (!eventData.TryGetProperty("performer", out var performers)) return [];
        var items = performers.ValueKind == JsonValueKind.Array
            ? performers.EnumerateArray().ToList()
            : [performers];
        return items
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new MeetupEventImportSpeaker(
                LimitLength(GetJsonLdString(item, "name")?.Trim(), 200) ?? string.Empty,
                LimitLength(GetJsonLdString(item, "description"), 4000),
                eventTitle))
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.Name))
            .DistinctBy(speaker => speaker.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetJsonLdLocation(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("location", out var location)) return null;
        if (location.ValueKind == JsonValueKind.String) return location.GetString();
        if (location.ValueKind != JsonValueKind.Object) return null;

        var name = GetJsonLdString(location, "name");
        if (location.TryGetProperty("address", out var address))
        {
            var addressText = address.ValueKind == JsonValueKind.String
                ? address.GetString()
                : address.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", new[] {
                        GetJsonLdString(address, "streetAddress"),
                        GetJsonLdString(address, "addressLocality"),
                        GetJsonLdString(address, "addressRegion"),
                        GetJsonLdString(address, "postalCode")
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : null;
            return string.Join(", ", new[] { name, addressText }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        return name;
    }

    private static string? GetJsonLdString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static bool IsMeetupHost(string host) =>
        host.Equals("meetup.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".meetup.com", StringComparison.OrdinalIgnoreCase);

    private static string FormatApiError(string? apiError) =>
        string.IsNullOrWhiteSpace(apiError) ? string.Empty : $" Meetup API fallback reason: {apiError}";

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
        if (eventNode.ValueKind == JsonValueKind.Object
            && eventNode.TryGetProperty("speakerDetails", out var speakerDetails)
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

        var speakerDescription = Regex.Replace(
            description,
            @"(?m)^\s*\*{1,2}(?<text>[^*\r\n]+?)\*{1,2}\s*$",
            "${text}");
        var speakerSection = Regex.Match(
            speakerDescription,
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

        var inferred = InferSpeakerFromTitle(eventTitle, speakerDescription);
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

        if (!string.IsNullOrWhiteSpace(speaker.FacebookUrl))
        {
            yield return ("Facebook", speaker.FacebookUrl);
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
