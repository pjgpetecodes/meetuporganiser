using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class Speaker
{
    public const string PlaceholderName = "Speaker TBC";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>True for a blank speaker created by the organiser, awaiting the speaker to complete their profile via magic link.</summary>
    public bool IsPlaceholder => Name == PlaceholderName;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(4000)]
    public string? Bio { get; set; }

    [MaxLength(4000)]
    public string? BioThirdPerson { get; set; }

    [MaxLength(50)]
    public string? Pronouns { get; set; }

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
    public string? FacebookUrl { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<EventSpeakerSession> Sessions { get; set; } = [];
    public List<MagicLinkToken> MagicLinkTokens { get; set; } = [];
}
