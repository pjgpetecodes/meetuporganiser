namespace MeetupOrganiser.Core.Entities;

public class ExtensionGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
