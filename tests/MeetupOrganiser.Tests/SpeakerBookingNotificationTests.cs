using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class SpeakerBookingNotificationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Notifications_UseCommittedEventDateAndReportDeliveryFailures(bool delivered)
    {
        var email = new RecordingEmailService(delivered);
        var settings = DispatchProxy.Create<ISettingsService, UnusedSettings>();
        var notifications = new SpeakerBookingNotifications(email, settings, NullLogger<SpeakerBookingNotifications>.Instance);
        var meetupEvent = new MeetupEvent
        {
            Title = "Selected meetup",
            EventDateTimeUtc = new DateTime(2027, 7, 19, 17, 30, 0, DateTimeKind.Utc),
            LocationOrPlatform = "Selected venue",
            MeetupGroup = new MeetupGroup { Name = "Selected group", OrganiserEmail = "organiser@example.com" }
        };
        var speaker = new Speaker { Name = "Ada", Email = "ada@example.com" };

        var result = await notifications.SendAsync(meetupEvent, speaker, "Selected talk", "https://meetup.example.com/");

        Assert.Equal(delivered, result);
        Assert.Equal(2, email.Messages.Count);
        Assert.Contains($"/events/edit/{meetupEvent.Id}", email.Messages[0].Body);
        var confirmation = email.Messages[1];
        Assert.Equal("ada@example.com", confirmation.To);
        Assert.Contains("Selected venue", confirmation.Body);
        Assert.Contains("Selected talk", confirmation.Body);
        Assert.Contains("DTSTART:20270719T173000Z", confirmation.Invite!.IcsContent);
        Assert.Equal("invite.ics", Assert.Single(confirmation.Attachments!).FileName);
    }

    private sealed class RecordingEmailService(bool delivered) : IEmailService
    {
        public List<(string To, string Body, IReadOnlyList<EmailAttachment>? Attachments, CalendarInvite? Invite)> Messages { get; } = [];

        public Task<bool> SendAsync(string to, string subject, string htmlBody, string? cc = null,
            IReadOnlyList<EmailAttachment>? attachments = null, CalendarInvite? invite = null, CancellationToken cancellationToken = default)
        {
            Messages.Add((to, htmlBody, attachments, invite));
            return Task.FromResult(delivered);
        }
    }

    public class UnusedSettings : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Group organiser address should take precedence over global settings.");
    }
}
