using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class MeetupEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MeetupGroupId { get; set; }
    public MeetupGroup? MeetupGroup { get; set; }

    [Required]
    [MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(8000)]
    public string? Description { get; set; }

    public DateTime EventDateTimeUtc { get; set; } = DateTime.UtcNow.AddDays(14);

    public int DurationMinutes { get; set; } = 90;

    [MaxLength(200)]
    public string? LocationOrPlatform { get; set; } = "Zoom";

    public EventFormat Format { get; set; } = EventFormat.Hybrid;

    /// <summary>Venue shown in the promo header for in-person and hybrid events (e.g. CGI).</summary>
    [MaxLength(100)]
    public string? VenueName { get; set; }

    /// <summary>Optional line under the date and venue on promo images and slides (e.g. "Special Workshop Edition!").</summary>
    [MaxLength(150)]
    public string? Subtitle { get; set; }

    [MaxLength(100)]
    public string? ZoomMeetingId { get; set; }

    [MaxLength(1000)]
    public string? ZoomMeetingUrl { get; set; }

    [MaxLength(50)]
    public string? ZoomPasscode { get; set; }

    [MaxLength(100)]
    public string? MeetupEventId { get; set; }

    [MaxLength(1000)]
    public string? MeetupEventUrl { get; set; }

    [MaxLength(500)]
    public string? ShortBitlyUrl { get; set; }

    public int? MeetupAttendeeCount { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Draft;
    public bool IsOpenForBookings { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<EventSpeakerSession> Sessions { get; set; } = [];
    public List<GeneratedAsset> GeneratedAssets { get; set; } = [];
}
