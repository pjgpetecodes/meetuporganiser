using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class EventSpeakerSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MeetupEventId { get; set; }
    public MeetupEvent? MeetupEvent { get; set; }

    public Guid SpeakerId { get; set; }
    public Speaker? Speaker { get; set; }

    [Required]
    [MaxLength(300)]
    public string TalkTitle { get; set; } = string.Empty;

    [MaxLength(6000)]
    public string? TalkDescription { get; set; }

    public int OrderIndex { get; set; } = 1;

    public List<MagicLinkToken> MagicLinkTokens { get; set; } = [];
}
