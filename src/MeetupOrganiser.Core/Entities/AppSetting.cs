namespace MeetupOrganiser.Core.Entities;

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
