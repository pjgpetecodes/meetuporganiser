using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class SocialPostTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Platform { get; set; } = string.Empty; // LinkedIn, Twitter, BlueSky, Meetup

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(4000)]
    public string TemplateText { get; set; } = string.Empty;

    public bool IsDefault { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
