using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        try
        {
            logger.LogInformation("Ensuring default group, speakers, events, and templates are seeded...");

            var nottsGroup = await context.MeetupGroups.FindAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"))
                ?? await context.MeetupGroups.FirstOrDefaultAsync(group => group.MeetupUrlName == "dotnetnotts");

            if (nottsGroup == null)
            {
                nottsGroup = new MeetupGroup
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = ".NET Notts",
                    Description = "Talks from the world of Microsoft and beyond. Nottingham's premier developer community meetup.",
                    WebsiteUrl = "https://dotnetnotts.co",
                    PrimaryColorHex = "#512BD4",
                    SecondaryColorHex = "#0078D4",
                    DefaultHashtags = "#dotnet #dotnetnotts #csharp #azure #developer",
                    DefaultLocation = "CGI, Nottingham & Online On Zoom",
                    MeetupUrlName = "dotnetnotts",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await context.MeetupGroups.AddAsync(nottsGroup);
            }

            var speakerLuce = await EnsureSpeakerAsync(context, new Speaker
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Luce Carter",
                Email = "luce@example.com",
                Bio = "Luce is a Senior Developer Advocate at MongoDB, Microsoft MVP for Developer Technologies, author of Beginning MongoDB Atlas with .NET and International conference speaker.\n\nShe loves learning, teaching, sunshine and playing squash.\n\nShe loves AI and always enjoys teaching people how you can combine AI with MongoDB easily in a way that makes sense, even for someone like her with Imposter Syndrome who jokes she is an expert beginner but not an expert in anything!",
                LinkedInUrl = "https://linkedin.com/in/lucecarter",
                TwitterHandle = "@lucecarter1",
                BlueSkyHandle = "lucecarter.bsky.social",
                GitHubHandle = "lucecarter",
                PhotoBlobUrl = "/images/speakers/Luce Carter.jpg",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            var speakerPete = await EnsureSpeakerAsync(context, new Speaker
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Pete Gallagher",
                Email = "pete@example.com",
                Bio = "Pete is a freelance programmer, Azure MVP, Azure Hero, and organizer of .NET Notts. Passionate about IoT, AI, and developer communities.",
                LinkedInUrl = "https://linkedin.com/in/pjgpetecodes",
                TwitterHandle = "@pete_codes",
                BlueSkyHandle = "petecodes.co.uk",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            // Default Templates
            var templates = new List<SocialPostTemplate>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Platform = "Meetup",
                    Name = "Standard Meetup Description",
                    TemplateText = "Join us for our upcoming {GroupName} session!\n\n**Date & Time:** {EventDate} at {EventTime}\n**Location:** {Location}\n\n**Talks & Speakers:**\n{SpeakerDetails}\n\nRSVP today to secure your spot!\n\n{Hashtags}",
                    IsDefault = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Platform = "LinkedIn",
                    Name = "LinkedIn Event Announcement",
                    TemplateText = "Exciting news! The next {GroupName} meetup is scheduled for {EventDate} at {EventTime}.\n\nWe are thrilled to welcome our speaker(s):\n{SpeakerDetails}\n\nRegister now on Meetup: {MeetupUrl}\n\n{Hashtags}",
                    IsDefault = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Platform = "Twitter",
                    Name = "X / Twitter Teaser",
                    TemplateText = "Our next @dotnetnotts meetup is on {EventDate}!\n\nFeaturing {SpeakerNames} presenting \"{TalkTitles}\".\n\nJoin us {Location}:\n{ShortUrl}\n\n{Hashtags}",
                    IsDefault = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Platform = "BlueSky",
                    Name = "BlueSky Announcement",
                    TemplateText = "Next {GroupName} event is live!\n\n📅 {EventDate} {EventTime}\n🎙️ Featuring: {SpeakerNames}\n💡 Talks: {TalkTitles}\n\nRSVP: {MeetupUrl}\n\n{Hashtags}",
                    IsDefault = true
                }
            };

            foreach (var template in templates)
            {
                var existingTemplate = await context.SocialPostTemplates
                    .FirstOrDefaultAsync(item => item.Platform == template.Platform && item.Name == template.Name);
                if (existingTemplate == null)
                {
                    await context.SocialPostTemplates.AddAsync(template);
                }
                else if (template.Platform == "Meetup"
                    && existingTemplate.IsDefault
                    && existingTemplate.TemplateText == "Join us for our upcoming {GroupName} session!\n\n**Date & Time:** {EventDate} at {EventTime}\n**Location:** {Location}\n\n**Talks & Speakers:**\n{SpeakerDetails}\n\nRSVP today to secure your spot!\n\n{Hashtags}")
                {
                    existingTemplate.TemplateText = "Join us for our upcoming {GroupName} session!\n\n**Date & Time:** {EventDate} at {EventTime}\n\n{SpeakerDetails}\n\n**How to find us:**\n{Location}\n\n{Hashtags}";
                }
            }

            // Sample Event
            var sampleEvent = await context.MeetupEvents
                .Include(meetupEvent => meetupEvent.Sessions)
                .FirstOrDefaultAsync(meetupEvent => meetupEvent.Id == Guid.Parse("44444444-4444-4444-4444-444444444444"));
            if (sampleEvent == null)
            {
                sampleEvent = new MeetupEvent
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    MeetupGroupId = nottsGroup.Id,
                    Title = "SQL to MongoDB API with C# - Luce Carter",
                    Description = "Join us for a hands-on workshop at CGI on King Street, Nottingham. Bring your laptop to take part in interactive exercises.",
                    EventDateTimeUtc = new DateTime(2026, 4, 27, 18, 30, 0, DateTimeKind.Utc),
                    DurationMinutes = 120,
                    LocationOrPlatform = "CGI, King Street Nottingham & Online",
                    Format = EventFormat.Hybrid,
                    VenueName = "CGI",
                    ShortBitlyUrl = "https://bit.ly/dotnetnottsapril2026",
                    MeetupEventUrl = "https://www.meetup.com/dotnetnotts/events/",
                    Status = EventStatus.Scheduled,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await context.MeetupEvents.AddAsync(sampleEvent);
            }
            else if (sampleEvent.Description?.Contains("Our Speaker:", StringComparison.Ordinal) == true)
            {
                sampleEvent.Description = "Join us for a hands-on workshop at CGI on King Street, Nottingham. Bring your laptop to take part in interactive exercises.";
                sampleEvent.UpdatedAtUtc = DateTime.UtcNow;
            }

            var sampleSession = sampleEvent.Sessions
                .FirstOrDefault(session => session.TalkTitle == "SQL to MongoDB API with C#");
            if (sampleSession == null)
            {
                sampleEvent.Sessions.Add(new EventSpeakerSession
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    MeetupEventId = sampleEvent.Id,
                    SpeakerId = speakerLuce.Id,
                    TalkTitle = "SQL to MongoDB API with C#",
                    TalkDescription = "Are you a C# developer? Get hands-on with the MongoDB Query API using the official C# driver. Learn to translate your RDBMS querying knowledge into MongoDB queries, explore CRUD operations, and adapt queries to document-model patterns.",
                    OrderIndex = 1
                });
            }
            else if (sampleSession.TalkDescription?.StartsWith("Are you a C# developer?", StringComparison.Ordinal) == true)
            {
                sampleSession.TalkDescription = "Are you a C# developer? Get hands-on with the MongoDB Query API using the official C# driver. Learn to translate your RDBMS querying knowledge into MongoDB queries, explore CRUD operations, and adapt queries to document-model patterns.";
            }

            if (!await context.AppSettings.AnyAsync())
            {
                var defaultSettings = SettingsService.GetDefaultSettings();
                await context.AppSettings.AddRangeAsync(defaultSettings);
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Default group, speakers, events, and templates are ready.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    private static async Task<Speaker> EnsureSpeakerAsync(AppDbContext context, Speaker defaultSpeaker)
    {
        var existingSpeaker = await context.Speakers.FindAsync(defaultSpeaker.Id)
            ?? await context.Speakers.FirstOrDefaultAsync(speaker => speaker.Name == defaultSpeaker.Name);
        if (existingSpeaker != null)
        {
            return existingSpeaker;
        }

        await context.Speakers.AddAsync(defaultSpeaker);
        return defaultSpeaker;
    }
}
