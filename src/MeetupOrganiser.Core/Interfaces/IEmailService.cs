namespace MeetupOrganiser.Core.Interfaces;

public interface IEmailService
{
    /// <summary>Sends an HTML email. Returns false when email is not configured.</summary>
    Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
