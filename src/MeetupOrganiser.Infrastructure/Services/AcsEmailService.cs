using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

/// <summary>
/// Sends email through Azure Communication Services. Uses the managed identity against
/// Email:Endpoint in Azure, or Email:ConnectionString for local development.
/// </summary>
public class AcsEmailService(IConfiguration configuration, ILogger<AcsEmailService> logger) : IEmailService
{
    public async Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var sender = configuration["Email:SenderAddress"];
        var connectionString = configuration["Email:ConnectionString"];
        var endpoint = configuration["Email:Endpoint"];

        if (string.IsNullOrWhiteSpace(sender) || (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(endpoint)))
        {
            logger.LogWarning("Email is not configured (Email:SenderAddress and Email:Endpoint or Email:ConnectionString); skipping email '{Subject}'.", subject);
            return false;
        }

        var client = !string.IsNullOrWhiteSpace(connectionString)
            ? new EmailClient(connectionString)
            : new EmailClient(new Uri(endpoint!), new DefaultAzureCredential());

        await client.SendAsync(WaitUntil.Started, sender, to, subject, htmlBody, plainTextContent: null, cancellationToken);
        return true;
    }
}
