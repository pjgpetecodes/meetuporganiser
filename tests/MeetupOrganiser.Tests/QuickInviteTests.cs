using System.Reflection;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class QuickInviteTests
{
    [Fact]
    public async Task DateChoiceInvites_DoNotCreateEventsOrReserveSessions()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var existing = new Speaker { Name = "Existing", Email = "existing@example.com" };
        db.Speakers.Add(existing);
        await db.SaveChangesAsync();
        var page = await CreatePageAsync(factory, db);
        var booking = DispatchProxy.Create<ISpeakerBookingService, InvitationServiceStub>();
        SetProperty(page, "BookingService", booking);
        SetField(page, "chooseDate", true);
        SetField(page, "speakerCount", 2);
        ChangeMode(page, 0, "Existing");
        Field<Guid?[]>(page, "selectedSpeakerIds")[0] = existing.Id;
        Field<Speaker[]>(page, "speakerDrafts")[1].Name = "New speaker";

        await InvokeAsync(page, "CreateInviteAsync");

        Assert.False(Field<bool>(page, "isError"));
        await using var verify = factory.CreateDbContext();
        Assert.Empty(await verify.MeetupEvents.ToListAsync());
        Assert.Empty(await verify.EventSpeakerSessions.ToListAsync());
        Assert.Equal(2, await verify.Speakers.CountAsync());
        Assert.Equal(2, ((InvitationServiceStub)booking).Calls);
        Assert.All(Field<List<string>>(page, "links"), link => Assert.Contains("/speaker/choose/", link));
        Assert.Null(Field<DateTime?>(page, "createdEventUtc"));
    }

    [Fact]
    public async Task ExistingAndImportedSpeakers_HaveSeparateLinksWithPrefilledEditableProfiles()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var existing = new Speaker
        {
            Name = "Existing Speaker", Email = "existing@example.com", Bio = "Existing bio",
            Pronouns = "they/them", PhotoBlobUrl = "https://example.com/existing.jpg",
            LinkedInUrl = "https://www.linkedin.com/in/existing"
        };
        db.Speakers.Add(existing);
        await db.SaveChangesAsync();
        var page = await CreatePageAsync(factory, db);
        SetField(page, "speakerCount", 2);
        ChangeMode(page, 0, "Existing");
        Field<Guid?[]>(page, "selectedSpeakerIds")[0] = existing.Id;
        ChangeMode(page, 1, "Import");
        Field<Speaker[]>(page, "speakerDrafts")[1].Email = "imported@example.com";
        Invoke(page, "ApplyProfile", 1, (Profile(), SessionizeProfileFields.All));

        await InvokeAsync(page, "CreateInviteAsync");

        Assert.False(Field<bool>(page, "isError"));
        await using var verify = factory.CreateDbContext();
        Assert.Equal(2, await verify.Speakers.CountAsync());
        var sessions = await verify.EventSpeakerSessions.OrderBy(s => s.OrderIndex).ToListAsync();
        Assert.Equal(existing.Id, sessions[0].SpeakerId);
        Assert.NotEqual(existing.Id, sessions[1].SpeakerId);
        Assert.Equal(new[] { "existing@example.com", "imported@example.com" }, Field<List<string?>>(page, "speakerEmailsSaved"));

        var links = Field<List<string>>(page, "links");
        Assert.Equal(2, links.Count);
        for (var slot = 0; slot < links.Count; slot++)
        {
            await using var portalDb = factory.CreateDbContext();
            var portal = new SpeakerSubmit();
            SetProperty(portal, "Token", links[slot].Split('/')[^1]);
            SetProperty(portal, "MagicLinkService", new MagicLinkService(portalDb));
            SetProperty(portal, "Logger", NullLogger<SpeakerSubmit>.Instance);
            await InvokeAsync(portal, "OnInitializedAsync");

            var speaker = Field<Speaker>(portal, "speaker");
            Assert.Equal(sessions[slot].SpeakerId, speaker.Id);
            Assert.False(Field<bool>(portal, "isNewProfile"));
            Assert.Equal(slot == 0 ? "Existing bio" : "Imported bio", speaker.Bio);
            Assert.Equal(slot == 0 ? existing.PhotoBlobUrl : Profile().PhotoUrl, speaker.PhotoBlobUrl);
            Assert.Equal(slot == 0 ? existing.LinkedInUrl : "https://www.linkedin.com/in/imported", speaker.LinkedInUrl);
            Assert.Equal(slot == 0 ? existing.Email : "imported@example.com", speaker.Email);

            speaker.Bio = $"Updated bio {slot}";
            speaker.Name = $"Updated name {slot}";
            await portalDb.SaveChangesAsync();
        }

        verify.ChangeTracker.Clear();
        Assert.Equal("Updated bio 0", (await verify.Speakers.FindAsync(existing.Id))!.Bio);
        Assert.Equal("Updated name 1", (await verify.Speakers.FindAsync(sessions[1].SpeakerId))!.Name);
    }

    [Theory]
    [InlineData("Existing", "Select an existing speaker")]
    [InlineData("Import", "Choose a Sessionize profile")]
    public async Task MissingSelection_DoesNotCreateEventOrSpeaker(string mode, string message)
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var page = await CreatePageAsync(factory, db);
        ChangeMode(page, 0, mode);

        await InvokeAsync(page, "CreateInviteAsync");

        Assert.True(Field<bool>(page, "isError"));
        Assert.StartsWith(message, Field<string>(page, "statusMessage"));
        Assert.Empty(await db.MeetupEvents.ToListAsync());
        Assert.Empty(await db.Speakers.ToListAsync());
    }

    [Fact]
    public async Task ChangingSource_ClearsOnlyThatSlotsPreviousProfile()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var page = await CreatePageAsync(factory, db);
        ChangeMode(page, 0, "Import");
        Invoke(page, "ApplyProfile", 0, (Profile(), SessionizeProfileFields.All));
        Field<Speaker[]>(page, "speakerDrafts")[1].Name = "Second speaker";

        ChangeMode(page, 0, "New");
        await InvokeAsync(page, "CreateInviteAsync");

        await using var verify = factory.CreateDbContext();
        var speaker = Assert.Single(await verify.Speakers.ToListAsync());
        Assert.True(speaker.IsPlaceholder);
        Assert.Null(speaker.Bio);
        Assert.Null(speaker.PhotoBlobUrl);
        Assert.Equal("Second speaker", Field<Speaker[]>(page, "speakerDrafts")[1].Name);
    }

    [Fact]
    public async Task ReplacingImportedProfile_WithNameOnlyProfile_DoesNotKeepPreviousDetails()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var page = await CreatePageAsync(factory, db);
        ChangeMode(page, 0, "Import");
        Field<Speaker[]>(page, "speakerDrafts")[0].Name = "Search text";
        Invoke(page, "ApplyProfile", 0, (Profile(), SessionizeProfileFields.All));
        var profile = Profile() with { Name = "Name Only", Bio = null, PhotoUrl = null, Links = [] };
        Invoke(page, "ApplyProfile", 0, (profile, SessionizeProfileFields.None));

        await InvokeAsync(page, "CreateInviteAsync");

        Assert.False(Field<bool>(page, "isError"));
        await using var verify = factory.CreateDbContext();
        var speaker = Assert.Single(await verify.Speakers.ToListAsync());
        Assert.Equal("Name Only", speaker.Name);
        Assert.False(speaker.IsPlaceholder);
        Assert.Null(speaker.Bio);
        Assert.Null(speaker.PhotoBlobUrl);
        Assert.Null(speaker.LinkedInUrl);
        Assert.Single(Field<List<string>>(page, "links"));
    }

    private static SessionizeProfile Profile() => new(
        "imported", "https://sessionize.com/imported", "Imported Speaker", null, null,
        "Imported bio", "https://example.com/imported.jpg",
        [new SessionizeLinkDto("LinkedIn", "https://www.linkedin.com/in/imported", "LinkedIn")], []);

    private static async Task<QuickInvite> CreatePageAsync(TestDbContextFactory factory, AppDbContext tokenDb)
    {
        await using var db = factory.CreateDbContext();
        db.MeetupGroups.Add(new MeetupGroup { Name = "Test group" });
        await db.SaveChangesAsync();
        var page = new QuickInvite();
        SetProperty(page, "DbContextFactory", factory);
        SetProperty(page, "MagicLinkService", new MagicLinkService(tokenDb));
        SetProperty(page, "NavManager", new TestNavigationManager());
        SetProperty(page, "Logger", NullLogger<QuickInvite>.Instance);
        await InvokeAsync(page, "OnInitializedAsync");
        return page;
    }

    private static void ChangeMode(QuickInvite page, int slot, string mode)
    {
        var enumType = typeof(QuickInvite).GetNestedType("SpeakerInviteMode", BindingFlags.NonPublic)!;
        Invoke(page, "ChangeSpeakerMode", slot, Enum.Parse(enumType, mode));
    }

    private static T Field<T>(object component, string name) =>
        (T)component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private static void SetField(object component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static void SetProperty(object component, string name, object value) =>
        component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);

    private static object? Invoke(object component, string method, params object[] arguments) =>
        component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);

    private static Task InvokeAsync(object component, string method) => (Task)Invoke(component, method)!;

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://meetup.example.com/", "https://meetup.example.com/events/invite");
    }

    public class InvitationServiceStub : DispatchProxy
    {
        public int Calls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(ISpeakerBookingService.CreateInvitationAsync), targetMethod?.Name);
            Calls++;
            return Task.FromResult($"invitation-{Calls}");
        }
    }
}
