using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class Speaker
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(4000)]
    public string? Bio { get; set; }

    [MaxLength(1000)]
    public string? PhotoBlobUrl { get; set; }

    [MaxLength(500)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(100)]
    public string? TwitterHandle { get; set; }

    [MaxLength(100)]
    public string? BlueSkyHandle { get; set; }

    [MaxLength(100)]
    public string? GitHubHandle { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<EventSpeakerSession> Sessions { get; set; } = [];
    public List<MagicLinkToken> MagicLinkTokens { get; set; } = [];
}
