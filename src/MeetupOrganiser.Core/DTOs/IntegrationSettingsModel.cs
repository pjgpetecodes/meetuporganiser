namespace MeetupOrganiser.Core.DTOs;

public class IntegrationSettingsModel
{
    // BlueSky
    public string? BlueSkyHandle { get; set; }
    public string? BlueSkyAppPassword { get; set; }

    // Zoom
    public string? ZoomAccountId { get; set; }
    public string? ZoomClientId { get; set; }
    public string? ZoomClientSecret { get; set; }

    // Meetup
    public string? MeetupUrlname { get; set; } = "dotnetnotts";
    public string? MeetupApiKey { get; set; }

    // Sessionize
    public string? SessionizeDefaultEventId { get; set; }

    // LinkedIn
    public string? LinkedInClientId { get; set; }
    public string? LinkedInClientSecret { get; set; }
    public string? LinkedInAccessToken { get; set; }
    public string? LinkedInAuthorUrn { get; set; }

    // Twitter / X
    public string? TwitterApiKey { get; set; }
    public string? TwitterApiSecret { get; set; }
    public string? TwitterAccessToken { get; set; }
    public string? TwitterAccessTokenSecret { get; set; }
}
