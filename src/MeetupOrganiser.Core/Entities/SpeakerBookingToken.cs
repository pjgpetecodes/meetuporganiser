namespace MeetupOrganiser.Core.Entities;

public class SpeakerBookingToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MeetupGroupId { get; set; }
    public MeetupGroup? MeetupGroup { get; set; }
    public Guid? SpeakerId { get; set; }
    public Speaker? Speaker { get; set; }
    public string? Email { get; set; }
    public string? PublicKey { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? BookedSessionId { get; set; }
    public EventSpeakerSession? BookedSession { get; set; }
}
