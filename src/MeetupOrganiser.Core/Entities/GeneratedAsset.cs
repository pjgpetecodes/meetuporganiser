using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class GeneratedAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MeetupEventId { get; set; }
    public MeetupEvent? MeetupEvent { get; set; }

    [Required]
    [MaxLength(50)]
    public string AssetType { get; set; } = string.Empty; // PPTX, SocialPng

    [Required]
    [MaxLength(250)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string BlobUrl { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
