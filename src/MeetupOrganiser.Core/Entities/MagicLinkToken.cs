using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class MagicLinkToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    public Guid SpeakerId { get; set; }
    public Speaker? Speaker { get; set; }

    public Guid EventSpeakerSessionId { get; set; }
    public EventSpeakerSession? EventSpeakerSession { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public bool IsRevoked { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsValid => !IsRevoked && UsedAtUtc == null && DateTime.UtcNow < ExpiresAtUtc;
}
