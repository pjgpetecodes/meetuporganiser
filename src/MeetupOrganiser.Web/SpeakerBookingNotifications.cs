using System.Text;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Web;

public sealed class SpeakerBookingNotifications(
    IEmailService emailService,
    ISettingsService settingsService,
    ILogger<SpeakerBookingNotifications> logger)
{
    public async Task<bool> SendAsync(MeetupEvent meetupEvent, Speaker speaker, string talkTitle, string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var sent = true;
        string? organiserEmail = meetupEvent.MeetupGroup?.OrganiserEmail;
        try
        {
            if (string.IsNullOrWhiteSpace(organiserEmail))
                organiserEmail = await settingsService.GetSettingAsync(SpeakerSubmittedEmail.OrganiserEmailSettingKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve booking notification recipient for event {EventId}.", meetupEvent.Id);
            sent = false;
        }

        if (!string.IsNullOrWhiteSpace(organiserEmail))
        {
            try
            {
                var editUrl = $"{baseUrl.TrimEnd('/')}/events/edit/{meetupEvent.Id}";
                var (subject, body) = SpeakerSubmittedEmail.Build(speaker.Name, talkTitle, meetupEvent.Title, editUrl);
                sent &= await emailService.SendAsync(organiserEmail, subject, body, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not send organiser booking notification for event {EventId}.", meetupEvent.Id);
                sent = false;
            }
        }

        if (!string.IsNullOrWhiteSpace(speaker.Email))
        {
            try
            {
                var (subject, body) = SpeakerConfirmationEmail.Build(speaker.Name, talkTitle, meetupEvent);
                var ics = IcsCalendar.Build(meetupEvent, organiserEmail: organiserEmail, attendeeEmail: speaker.Email, attendeeName: speaker.Name);
                var attachment = new EmailAttachment("invite.ics", "text/calendar", Encoding.UTF8.GetBytes(ics));
                sent &= await emailService.SendAsync(speaker.Email, subject, body, cc: organiserEmail, attachments: [attachment],
                    invite: string.IsNullOrWhiteSpace(organiserEmail) ? null : new CalendarInvite(ics), cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not send speaker booking confirmation for event {EventId}.", meetupEvent.Id);
                sent = false;
            }
        }
        if (!sent) logger.LogWarning("One or more booking emails were not delivered for event {EventId}.", meetupEvent.Id);
        return sent;
    }
}
