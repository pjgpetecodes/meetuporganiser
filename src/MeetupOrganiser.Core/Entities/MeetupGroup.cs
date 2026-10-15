using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class MeetupGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(1000)]
    public string? LogoBlobUrl { get; set; }

    [MaxLength(1000)]
    public string? PptxTemplateBlobUrl { get; set; }

    [MaxLength(20)]
    public string PrimaryColorHex { get; set; } = "#512BD4"; // .NET Purple default

    [MaxLength(20)]
    public string SecondaryColorHex { get; set; } = "#107C41";

    [MaxLength(500)]
    public string? DefaultHashtags { get; set; } = "#meetup #dotnet";

    [MaxLength(200)]
    public string? DefaultLocation { get; set; } = "On Zoom";

    [MaxLength(100)]
    public string? MeetupUrlName { get; set; }

    // Notified when a speaker completes their magic-link form; blank = use the global setting
    [MaxLength(320)]
    public string? OrganiserEmail { get; set; }

    // e.g. https://bit.ly/dotnetnotts; the event month and year are appended per event
    [MaxLength(200)]
    public string? ShortUrlStem { get; set; }

    // Announcement text for X, Bluesky and LinkedIn; see SocialPostTemplate for placeholders
    [MaxLength(2000)]
    public string? SocialPostTemplate { get; set; }

    [MaxLength(500)]
    public string? MeetupApiKeyOverride { get; set; }

    // null/blank = use the global setting
    [MaxLength(20)]
    public string? BioPreference { get; set; }

    [MaxLength(8000)]
    public string? MeetupDescriptionTemplate { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<MeetupEvent> Events { get; set; } = [];
    public List<Sponsor> Sponsors { get; set; } = [];
}
