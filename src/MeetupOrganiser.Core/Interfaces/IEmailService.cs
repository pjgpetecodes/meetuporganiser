namespace MeetupOrganiser.Core.Interfaces;

public interface IEmailService
{
    /// <summary>Sends an HTML email. Returns false when email is not configured.</summary>
    Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? cc = null,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CalendarInvite? invite = null,
        CancellationToken cancellationToken = default);
}

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>An iCalendar METHOD:REQUEST body to embed inline so mail clients show Accept/Decline buttons.</summary>
public record CalendarInvite(string IcsContent);
