using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class DbInitializerTests
{
    private const string LegacyMeetupTemplate = "Join us for our upcoming {GroupName} session!\n\n**Date & Time:** {EventDate} at {EventTime}\n**Location:** {Location}\n\n**Talks & Speakers:**\n{SpeakerDetails}\n\nRSVP today to secure your spot!\n\n{Hashtags}";

    [Fact]
    public async Task SeedAsync_RestoresDefaultDataWithoutRemovingUserData_AndIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        var customGroup = new MeetupGroup { Name = "Custom Group", MeetupUrlName = "custom-group" };
        var customSpeaker = new Speaker { Name = "Custom Speaker" };
        var customEvent = new MeetupEvent
        {
            MeetupGroupId = customGroup.Id,
            Title = "Custom Event"
        };
        await context.MeetupGroups.AddAsync(customGroup);
        await context.Speakers.AddAsync(customSpeaker);
        await context.MeetupEvents.AddAsync(customEvent);
        await context.SaveChangesAsync();

        await DbInitializer.SeedAsync(context, NullLogger.Instance);
        await DbInitializer.SeedAsync(context, NullLogger.Instance);

        Assert.Equal(2, await context.MeetupGroups.CountAsync());
        Assert.Equal(3, await context.Speakers.CountAsync());
        Assert.Equal(2, await context.MeetupEvents.CountAsync());
        Assert.Equal(4, await context.SocialPostTemplates.CountAsync());
        Assert.Single(await context.EventSpeakerSessions.ToListAsync());
        Assert.Equal("Custom Group", (await context.MeetupGroups.FindAsync(customGroup.Id))?.Name);
        Assert.Equal("Custom Speaker", (await context.Speakers.FindAsync(customSpeaker.Id))?.Name);
        Assert.Equal("Custom Event", (await context.MeetupEvents.FindAsync(customEvent.Id))?.Title);

        var meetupTemplate = await context.SocialPostTemplates
            .SingleAsync(template => template.Platform == "Meetup");
        Assert.Contains("How to find us:", meetupTemplate.TemplateText);
        Assert.Contains("{SpeakerDetails}", meetupTemplate.TemplateText);
    }

    [Fact]
    public async Task SeedAsync_UpdatesLegacyMeetupDefaultsAndSampleDescription()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        var group = new MeetupGroup
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = ".NET Notts",
            MeetupUrlName = "dotnetnotts"
        };
        var speaker = new Speaker
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Luce Carter"
        };
        var sampleEvent = new MeetupEvent
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            MeetupGroupId = group.Id,
            Title = "SQL to MongoDB API with C# - Luce Carter",
            Description = "Legacy event copy.\n\nOur Speaker:\nLuce Carter:\n",
            Sessions =
            [
                new EventSpeakerSession
                {
                    Id = Guid.NewGuid(),
                    SpeakerId = speaker.Id,
                    TalkTitle = "SQL to MongoDB API with C#",
                    TalkDescription = "Are you a C# developer? Legacy talk copy."
                }
            ]
        };
        await context.MeetupGroups.AddAsync(group);
        await context.Speakers.AddAsync(speaker);
        await context.MeetupEvents.AddAsync(sampleEvent);
        await context.SocialPostTemplates.AddAsync(new SocialPostTemplate
        {
            Platform = "Meetup",
            Name = "Standard Meetup Description",
            TemplateText = LegacyMeetupTemplate,
            IsDefault = true
        });
        await context.SaveChangesAsync();

        await DbInitializer.SeedAsync(context, NullLogger.Instance);

        var meetupTemplate = await context.SocialPostTemplates.SingleAsync(template => template.Platform == "Meetup");
        Assert.Contains("How to find us:", meetupTemplate.TemplateText);
        Assert.DoesNotContain("RSVP today", meetupTemplate.TemplateText);

        var seededEvent = await context.MeetupEvents.Include(item => item.Sessions)
            .SingleAsync(item => item.Id == sampleEvent.Id);
        Assert.Contains("hands-on workshop", seededEvent.Description);
        Assert.DoesNotContain("Our Speaker:", seededEvent.Description);
        Assert.Contains("Get hands-on with the MongoDB Query API", seededEvent.Sessions.Single().TalkDescription);
    }
}
