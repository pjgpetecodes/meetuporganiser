using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class CalendarInviteEmailTests
{
    private static MeetupEvent Event() => new()
    {
        Title = "June Meetup",
        EventDateTimeUtc = new DateTime(2026, 6, 10, 17, 30, 0, DateTimeKind.Utc),
        DurationMinutes = 90,
        VenueName = "CGI",
        LocationOrPlatform = "Nottingham"
    };

    [Fact]
    public void Smtp_Message_HasInlineRequestAndAttachment()
    {
        var invite = new CalendarInvite("BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nEND:VCALENDAR\r\n");
        var attachment = new EmailAttachment("invite.ics", "text/calendar", [1, 2, 3]);

        var message = AcsSmtpEmailService.BuildMessage("noreply@example.com", "ada@example.com", "Hi", "<p>x</p>", "org@example.com", [attachment], invite);
        var raw = message.ToString();

        Assert.Equal("org@example.com", message.Cc.Mailboxes.Single().Address);
        Assert.Contains("multipart/alternative", raw);
        Assert.Contains("text/calendar; charset=utf-8; method=REQUEST", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filename=invite.ics", raw);
    }

    [Fact]
    public void Links_ContainEncodedEventDetails()
    {
        var google = CalendarLinks.Google(Event());
        var outlook = CalendarLinks.Outlook(Event());

        Assert.Contains("text=June%20Meetup", google);
        Assert.Contains("dates=20260610T173000Z/20260610T190000Z", google);
        Assert.Contains("location=CGI%2C%20Nottingham", google);
        Assert.Contains("startdt=2026-06-10T17%3A30%3A00Z", outlook);
    }

    [Fact]
    public void Confirmation_IncludesCalendarLinks()
    {
        var (_, body) = SpeakerConfirmationEmail.Build("Ada", "Talk", Event());

        Assert.Contains("calendar.google.com", body);
        Assert.Contains("outlook.live.com", body);
    }

    [Fact]
    public async Task Router_WithoutSmtpConfig_FallsBackToApiAndDoesNotThrow()
    {
        var config = new ConfigurationBuilder().Build();
        var smtp = new AcsSmtpEmailService(config, NullLogger<AcsSmtpEmailService>.Instance);
        var service = new EmailService(new AcsEmailService(config, NullLogger<AcsEmailService>.Instance), smtp, NullLogger<EmailService>.Instance);

        Assert.False(smtp.IsConfigured);
        Assert.False(await service.SendAsync("a@example.com", "s", "<p>b</p>", invite: new CalendarInvite("x")));
    }
}
