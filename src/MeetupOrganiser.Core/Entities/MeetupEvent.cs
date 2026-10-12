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

    public EventStatus Status { get; set; } = EventStatus.Draft;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<EventSpeakerSession> Sessions { get; set; } = [];
    public List<GeneratedAsset> GeneratedAssets { get; set; } = [];
}
