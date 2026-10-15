using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

/// <summary>
/// Routes calendar-invite emails through SMTP (inline RSVP) when configured, and everything else
/// (or any SMTP failure) through the ACS Email API.
/// </summary>
public class EmailService(AcsEmailService api, AcsSmtpEmailService smtp, ILogger<EmailService> logger) : IEmailService
{
    public async Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? cc = null,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CalendarInvite? invite = null,
        CancellationToken cancellationToken = default)
    {
        if (invite != null && smtp.IsConfigured)
        {
            try
            {
                return await smtp.SendAsync(to, subject, htmlBody, cc, attachments, invite, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SMTP calendar invite failed; falling back to the ACS Email API.");
            }
        }

        return await api.SendAsync(to, subject, htmlBody, cc, attachments, null, cancellationToken);
    }
}
