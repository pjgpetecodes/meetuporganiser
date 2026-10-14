using System.ComponentModel.DataAnnotations;

namespace MeetupOrganiser.Core.Entities;

public class Sponsor
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MeetupGroupId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? LogoBlobUrl { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(200)]
    public string? TwitterHandle { get; set; }

    [MaxLength(200)]
    public string? BlueSkyHandle { get; set; }

    [MaxLength(500)]
    public string? LinkedInUrl { get; set; }

    public int SortOrder { get; set; }
}
