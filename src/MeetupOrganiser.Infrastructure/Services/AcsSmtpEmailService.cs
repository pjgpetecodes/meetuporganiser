using MailKit.Net.Smtp;
using MailKit.Security;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace MeetupOrganiser.Infrastructure.Services;

/// <summary>
/// Sends email through the Azure Communication Services SMTP endpoint so a calendar request can be
/// embedded inline (text/calendar; method=REQUEST), which makes mail clients show Accept/Decline buttons.
/// </summary>
public class AcsSmtpEmailService(IConfiguration configuration, ILogger<AcsSmtpEmailService> logger)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Email:SenderAddress"])
        && !string.IsNullOrWhiteSpace(configuration["Email:Smtp:Username"])
        && !string.IsNullOrWhiteSpace(configuration["Email:Smtp:Password"]);

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? cc,
        IReadOnlyList<EmailAttachment>? attachments,
        CalendarInvite invite,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        var message = BuildMessage(configuration["Email:SenderAddress"]!, to, subject, htmlBody, cc, attachments, invite);

        var host = configuration["Email:Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host)) host = "smtp.azurecomm.net";

        using var client = new SmtpClient();
        await client.ConnectAsync(host, 587, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(configuration["Email:Smtp:Username"], configuration["Email:Smtp:Password"], cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        logger.LogInformation("Sent calendar invite email '{Subject}' via SMTP.", subject);
        return true;
    }

    public static MimeMessage BuildMessage(
        string sender,
        string to,
        string subject,
        string htmlBody,
        string? cc,
        IReadOnlyList<EmailAttachment>? attachments,
        CalendarInvite invite)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(to));
        if (!string.IsNullOrWhiteSpace(cc) && !string.Equals(cc, to, StringComparison.OrdinalIgnoreCase))
            message.Cc.Add(MailboxAddress.Parse(cc));
        message.Subject = subject;

        var calendar = new TextPart("calendar") { Text = invite.IcsContent };
        calendar.ContentType.Parameters.Add("method", "REQUEST");

        var alternative = new MultipartAlternative { new TextPart("html") { Text = htmlBody }, calendar };

        var body = new Multipart("mixed") { alternative };
        foreach (var attachment in attachments ?? [])
        {
            var type = ContentType.Parse(attachment.ContentType);
            body.Add(new MimePart(type)
            {
                Content = new MimeContent(new MemoryStream(attachment.Content)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = attachment.FileName
            });
        }

        message.Body = body;
        return message;
    }
}
