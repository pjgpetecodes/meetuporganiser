using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace MeetupOrganiser.Tests;

public class TemplateSectionTests
{
    [Fact]
    public void DefaultSections_PreserveGeneratedDescriptionAndCustomSectionsNest()
    {
        var meetup = new MeetupEvent { Title = "A meetup", Description = "Agenda", EventDateTimeUtc = DateTime.UtcNow };
        var service = new MeetupService(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<MeetupService>.Instance);
        var sections = Defaults();
        var original = service.GenerateMeetupDescription(meetup, template: MeetupDescriptionTemplate.Default);
        Assert.Equal(original, service.GenerateMeetupDescription(meetup, template: MeetupDescriptionTemplate.Default, sections: sections));
        sections.Single(s => s.Name == "EventTitle").Markdown = "**{{Value}}**";
        sections.Add(new TemplateSection { Name = "Welcome", Markdown = "Welcome to {{eventtitle}}!" });
        Assert.Equal("Welcome to **A meetup**!\n", service.GenerateMeetupDescription(meetup, template: "{{WELCOME}}", sections: sections));
        Assert.Equal("{{Removed}}\n", MeetupDescriptionTemplate.Render("{{Removed}}", new Dictionary<string, string>(), []));
    }

    [Theory]
    [InlineData("{{Missing}}", "missing section")]
    [InlineData("{{Custom}}", "circular reference")]
    [InlineData("{{Value}}", "no automatic Value")]
    public void InvalidReferences_AreExplicit(string markdown, string error)
    {
        var ex = Assert.Throws<ArgumentException>(() => MeetupDescriptionTemplate.ValidateSections(
            [new TemplateSection { Name = "Custom", Markdown = markdown }]));
        Assert.Contains(error, ex.Message);
    }

    [Fact]
    public void EditableBlocks_RepeatWithCurrentTalkAndSpeakerValuesAndPreserveMissingDataRules()
    {
        var sections = Defaults();
        void Set(string name, string markdown) => sections.Single(s => s.Name == name).Markdown = markdown;
        Set("TalkBlock", "**{{TalkTitle}}** by {{SpeakerName}}\n\n{{TalkDescription}}");
        Set("TalkDescriptionBlock", "Abstract: {{TalkDescription}}");
        Set("MultipleSpeakersHeading", "**Meet our speakers**");
        Set("SpeakerBlock", "Introducing {{SpeakerName}}\n\n{{SpeakerBio}}\n\n{{SpeakerContacts}}");
        Set("SpeakerContactsBlock", "Connect with {{SpeakerFirstName}}:\n\n{{SpeakerLinks}}");
        Set("XLink", "Follow: {{ContactUrl}}");
        Set("ZoomLinkBlock", "Watch live: {{ZoomUrl}}");
        Set("ZoomPasscodeBlock", "Access code: {{ZoomPasscode}}");
        var first = new Speaker { Name = "Alex Example", Bio = "Alex builds apps.", TwitterHandle = "@alex" };
        var second = new Speaker { Name = "Sam Example", Bio = "Sam writes tests." };
        var meetup = new MeetupEvent
        {
            Title = "Two talks", ZoomMeetingUrl = "https://zoom.example/join", ZoomPasscode = "secret",
            Sessions = [
                new() { OrderIndex = 2, TalkTitle = "Testing", TalkDescription = "Testing abstract.", Speaker = second },
                new() { OrderIndex = 1, TalkTitle = "Building", TalkDescription = "Building abstract.", Speaker = first }
            ]
        };
        var service = new MeetupService(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<MeetupService>.Instance);
        const string template = "{{Talks}}\n\n{{TalkDescriptions}}\n\n{{SpeakerSection}}\n\n{{ZoomSection}}";
        var rendered = service.GenerateMeetupDescription(meetup, "original", template, sections);
        Assert.Contains("**Building** by Alex Example\n\nBuilding abstract.", rendered);
        Assert.Contains("**Testing** by Sam Example\n\nTesting abstract.", rendered);
        Assert.True(rendered.IndexOf("**Building**", StringComparison.Ordinal) < rendered.IndexOf("**Testing**", StringComparison.Ordinal));
        Assert.Contains("Abstract: Building abstract.", rendered);
        Assert.Contains("**Meet our speakers**", rendered);
        Assert.Contains("Introducing Alex Example\n\nAlex builds apps.\n\nConnect with Alex:\n\nFollow: https://x.com/alex", rendered);
        Assert.Contains("Introducing Sam Example\n\nSam writes tests.", rendered);
        Assert.DoesNotContain("Connect with Sam", rendered);
        Assert.Equal(1, rendered.Split("Follow:").Length - 1);
        Assert.Contains("Watch live: https://zoom.example/join\nAccess code: secret", rendered);
        Assert.DoesNotContain("Our Speakers:", rendered);
        Assert.DoesNotContain("How to find", rendered);
        Assert.DoesNotContain("Online on Zoom:", rendered);
        meetup.ZoomPasscode = null;
        Assert.DoesNotContain("Access code:", service.GenerateMeetupDescription(meetup, "original", template, sections));
        meetup.ZoomMeetingUrl = null;
        meetup.Sessions = [];
        Assert.Equal("\n", service.GenerateMeetupDescription(meetup, "original", template, sections));
    }

    [Fact]
    public void DefaultBlocks_PreserveExistingSpeakerContactAndZoomWording()
    {
        var meetup = new MeetupEvent
        {
            Title = "Meetup", ZoomMeetingUrl = "https://zoom.example/join", ZoomPasscode = "123",
            Sessions = [new() { TalkTitle = "Talk", TalkDescription = "Abstract.", Speaker =
                new() { Name = "Alex Example", Bio = "Alex builds apps.", TwitterHandle = "alex", WebsiteUrl = "https://alex.example" } }]
        };
        var service = new MeetupService(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<MeetupService>.Instance);
        Assert.Equal("""
            Talk - Alex Example

            Abstract.

            Our Speaker:

            Alex Example:

            Alex builds apps.

            How to find Alex:

            X: https://x.com/alex

            Personal Site: https://alex.example

            Online on Zoom: https://zoom.example/join
            Passcode: 123
            """.Replace("\r\n", "\n") + "\n",
            service.GenerateMeetupDescription(meetup, "original", "{{Talks}}\n\n{{SpeakerSection}}\n\n{{ZoomSection}}", Defaults()));
    }

    [Fact]
    public void Validation_RejectsIndirectCyclesDuplicateNamesAndInvalidNames()
    {
        Assert.Throws<ArgumentException>(() => MeetupDescriptionTemplate.ValidateSections([
            new() { Name = "First", Markdown = "{{Second}}" }, new() { Name = "Second", Markdown = "{{first}}" }]));
        Assert.Throws<ArgumentException>(() => MeetupDescriptionTemplate.ValidateSections([
            new() { Name = "First", Markdown = "" }, new() { Name = "FIRST", Markdown = "" }]));
        foreach (var name in new[] { "", "1Name", "With space", "Value", new string('a', 65) })
            Assert.Throws<ArgumentException>(() => MeetupDescriptionTemplate.ValidateSections([new() { Name = name, Markdown = "" }]));
    }

    [Fact]
    public void Expansion_BoundsBothSectionsAndFinalDescription()
    {
        var sections = new List<TemplateSection> { new() { Name = "Chunk", Markdown = new string('a', 40000) } };
        var values = new Dictionary<string, string>();
        Assert.Throws<ArgumentException>(() => MeetupDescriptionTemplate.Render("{{Chunk}}{{Chunk}}", values, sections));
        sections.Add(new() { Name = "Combined", Markdown = "{{Chunk}}{{Chunk}}" });
        Assert.Throws<ArgumentException>(() => MeetupDescriptionTemplate.Render("{{Combined}}", values, sections));
    }

    [Fact]
    public async Task Crud_PersistsAcrossContextsAndRejectsInvalidChangesWithoutLosingSavedContent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new AppDbContext(options)) await db.Database.EnsureCreatedAsync();
        await AssertCrudAsync(options);
    }

    [BookingSqlFact]
    public async Task AdditiveMigration_PreservesDataSeedsSectionsAndDoesNotRestoreDeletedSections()
    {
        await using var database = await BookingSqlDatabase.CreateAsync(migrate: false);
        var eventId = Guid.NewGuid();
        await using (var db = new AppDbContext(database.Options))
        {
            var migrations = db.Database.GetMigrations().ToList();
            await db.GetService<IMigrator>().MigrateAsync(migrations.Single(m => m.EndsWith("_AddExtensionGrants")));
            db.MeetupEvents.Add(new() { Id = eventId, Title = "Existing meetup", EventDateTimeUtc = DateTime.UtcNow,
                MeetupGroup = new() { Name = "Existing group" } });
            await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync(migrations.Single(m => m.EndsWith("_AddTemplateSections")));
            var existingSection = await db.TemplateSections.SingleAsync(s => s.Name == "SpeakerSection");
            existingSection.Markdown = "**Speakers**\n\n{{Value}}";
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            Assert.Equal("Existing meetup", (await db.MeetupEvents.SingleAsync(e => e.Id == eventId)).Title);
            Assert.Equal("**Speakers**\n\n{{Value}}",
                (await db.TemplateSections.AsNoTracking().SingleAsync(s => s.Name == "SpeakerSection")).Markdown);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        await AssertCrudAsync(database.Options);
        await using var restarted = new AppDbContext(database.Options);
        await restarted.Database.MigrateAsync();
        Assert.False(await restarted.TemplateSections.AnyAsync(s => s.Name == "EventTitle"));
    }

    private static async Task AssertCrudAsync(DbContextOptions<AppDbContext> options)
    {
        var factory = new BookingContextFactory(options);
        var service = new TemplateSectionService(factory);
        Assert.Equal(MeetupDescriptionTemplate.CreateDefaultSections().Count, (await service.ListAsync()).Count);
        var custom = new TemplateSection { Name = "Welcome", Description = "Opening", Markdown = "**Hello {{EventTitle}}**" };
        await service.SaveAsync(custom);
        Assert.Equal(custom.Markdown, (await new TemplateSectionService(factory).ListAsync()).Single(s => s.Id == custom.Id).Markdown);
        var title = (await service.ListAsync()).Single(s => s.Name == "EventTitle");
        title.Markdown = "**{{Value}}**";
        await service.SaveAsync(title);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(title.Id));
        Assert.Contains("EventTitle", (await service.ListAsync()).Select(s => s.Name));
        custom.Markdown = "{{NotDefined}}";
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(custom));
        Assert.Equal("**Hello {{EventTitle}}**", (await service.ListAsync()).Single(s => s.Id == custom.Id).Markdown);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new() { Name = "welcome", Markdown = "" }));
        await service.DeleteAsync(custom.Id);
        await service.DeleteAsync(title.Id);
        Assert.DoesNotContain(await new TemplateSectionService(factory).ListAsync(), s => s.Id == custom.Id || s.Id == title.Id);
    }

    [Fact]
    public async Task ReservedNames_RejectCustomCreationAndRenamingEvenAfterDeletion()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new AppDbContext(options)) await db.Database.EnsureCreatedAsync();
        var service = new TemplateSectionService(new BookingContextFactory(options));
        foreach (var name in MeetupDescriptionTemplate.CreateDefaultSections().Select(p => p.Name).Append("Value"))
        {
            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SaveAsync(new() { Name = $" {name.ToUpperInvariant()} ", Markdown = "" }));
            Assert.Contains("reserved", error.Message);
        }
        var custom = new TemplateSection { Name = "Welcome", Markdown = "Hello" };
        await service.SaveAsync(custom);
        custom.Name = "EventTitle";
        Assert.Contains("reserved", (await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(custom))).Message);
        var title = (await service.ListAsync()).Single(s => s.Name == "EventTitle");
        title.Name = "RenamedTitle";
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(title));
        title.Name = "EventTitle";
        title.Markdown = "**{{Value}}**";
        await service.SaveAsync(title);
        Assert.Equal(title.Markdown, (await service.ListAsync()).Single(s => s.Id == title.Id).Markdown);
        var block = (await service.ListAsync()).Single(s => s.Name == "SpeakerBlock");
        block.Markdown = "**{{SpeakerName}}**\n\n{{SpeakerBio}}";
        await service.SaveAsync(block);
        Assert.Equal(block.Markdown, (await service.ListAsync()).Single(s => s.Id == block.Id).Markdown);
        await service.DeleteAsync(title.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new() { Name = "eventtitle", Markdown = "Replacement" }));
        Assert.Equal("Welcome", (await service.ListAsync()).Single(s => s.Id == custom.Id).Name);
    }

    [Fact]
    public async Task Editor_RendersDatabaseSectionAndRichEditor()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .AddScoped<ITemplateSectionService, TemplateSectionService>()
            .AddSingleton(DispatchProxy.Create<IJSRuntime, CfpOrganiserRenderingTests.UnusedService>())
            .BuildServiceProvider();
        await using (var db = services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            db.TemplateSections.Add(new() { Name = "Welcome", Markdown = "**Hello**" });
            await db.SaveChangesAsync();
        }
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TemplateSectionEditor>()).ToHtmlString());
        Assert.Contains(">Welcome</option>", html);
        Assert.Contains("contenteditable=\"true\"", html);
        Assert.Contains("<strong>Hello</strong>", html);
        Assert.Contains("Save section", html);
        Assert.Contains("Delete section", html);
    }

    private static List<TemplateSection> Defaults() => MeetupDescriptionTemplate.CreateDefaultSections();
}
