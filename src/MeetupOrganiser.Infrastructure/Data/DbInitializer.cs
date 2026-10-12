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
            if (await context.MeetupGroups.AnyAsync())
            {
                logger.LogInformation("Database already seeded with initial data.");
                return;
            }

            logger.LogInformation("Seeding database with default group, speakers, and templates...");

            // Default Group (.NET Notts)
            var nottsGroup = new MeetupGroup
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = ".NET Notts",
                Description = "Talks from the world of Microsoft and beyond. Nottingham's premier developer community meetup.",
                WebsiteUrl = "https://dotnetnotts.co",
                PrimaryColorHex = "#512BD4",
                SecondaryColorHex = "#0078D4",
                DefaultHashtags = "#dotnet #dotnetnotts #csharp #azure #developer",
                DefaultLocation = "CGI, Nottingham & Online On Zoom",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await context.MeetupGroups.AddAsync(nottsGroup);

            // Default Speakers
            var speakerLuce = new Speaker
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
            };

            var speakerPete = new Speaker
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
            };

            await context.Speakers.AddRangeAsync(speakerLuce, speakerPete);

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

            await context.SocialPostTemplates.AddRangeAsync(templates);

            // Sample Event
            var sampleEvent = new MeetupEvent
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                MeetupGroupId = nottsGroup.Id,
                Title = "SQL to MongoDB API with C# - Luce Carter",
                Description = "This will be a special Workshop Edition and in-person only event at CGI (used to be BJSS) on King Street Nottingham.\n\nSQL to MongoDB API with C# - Luce Carter\n\nAre you a C# developer? Are you ready to get hands-on and write queries using the MongoDB Query API using the official C# driver?\n\nJoin us for an engaging workshop that will take you on a transformative journey, allowing you to translate your existing RDBMS querying knowledge into new MongoDB Query API skills.\n\nThrough interactive exercises, you will skillfully transition your RDBMS expertise into the MongoDB Query API.\n\nYou’ll delve into the fundamentals of CRUD operations, and create new queries adapted to the document-model patterns used in MongoDB.\n\nWe can't wait to see you there!\n\nHooks:\n\nTransform your SQL skills into MongoDB expertise and unlock the power of flexible, server-side analytics!\n\nLevel up your querying game by utilizing MongoDB's powerful and scalable document-based model!\n\nDuring the webinar/workshop, you’ll have the opportunity to earn the “MongoDB CRUD Operations: C#” skill badge that you can add to your LinkedIn profile.\n\nYou'll need your laptop to fully participate in this hands-on session. 💻\n\nOur Speaker:\n\nLuce Carter:\n\nLuce is a Senior Developer Advocate at MongoDB, Microsoft MVP for Developer Technologies, author of Beginning MongoDB Atlas with .NET and International conference speaker.\n\nShe loves learning, teaching, sunshine and playing squash.\n\nShe loves AI and always enjoys teaching people how you can combine AI with MongoDB easily in a way that makes sense, even for someone like her with Imposter Syndrome who jokes she is an expert beginner but not an expert in anything!\n\nHow to find Luce:\n\nX: https://x.com/lucecarter1\nLinkedIn: https://linkedin.com/in/lucecarter\nGitHub: https://github.com/lucecarter\n\nHow to find us:\n\nWe'll be in person at CGI on King Street Nottingham opposite the Brian Clough Statue from 6.30PM UK Time.",
                EventDateTimeUtc = new DateTime(2026, 4, 27, 18, 30, 0, DateTimeKind.Utc),
                DurationMinutes = 120,
                LocationOrPlatform = "CGI, King Street Nottingham & Online",
                ShortBitlyUrl = "https://bit.ly/dotnetnottsapril2026",
                MeetupEventUrl = "https://www.meetup.com/dotnetnotts/events/",
                Status = EventStatus.Scheduled,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var session1 = new EventSpeakerSession
            {
                Id = Guid.NewGuid(),
                MeetupEventId = sampleEvent.Id,
                SpeakerId = speakerLuce.Id,
                TalkTitle = "SQL to MongoDB API with C#",
                TalkDescription = "Are you a C# developer? Are you ready to get hands-on and write queries using the MongoDB Query API using the official C# driver?\n\nJoin us for an engaging workshop that will take you on a transformative journey, allowing you to translate your existing RDBMS querying knowledge into new MongoDB Query API skills.",
                OrderIndex = 1
            };

            sampleEvent.Sessions.Add(session1);
            await context.MeetupEvents.AddAsync(sampleEvent);

            if (!await context.AppSettings.AnyAsync())
            {
                var defaultSettings = SettingsService.GetDefaultSettings();
                await context.AppSettings.AddRangeAsync(defaultSettings);
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Database seeding completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
