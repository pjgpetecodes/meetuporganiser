using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class EventEditDescriptionTests
{
    [Fact]
    public async Task EnteringTalkDetails_AutoPopulatesAndUpdatesDescriptionWhilePreservingManualEdits()
    {
        var component = await CreateAsync();
        var meetup = Field<MeetupEvent>(component, "evt");
        var session = Field<EventSpeakerSession>(component, "session1");
        var speaker = Field<List<Speaker>>(component, "speakers")[0];
        await Call(component, "OnSpeakerChangedAsync", 0, speaker.Id);
        Assert.Contains("Alex Example", meetup.Description);
        session.TalkTitle = "First talk";
        await Call(component, "RefreshMeetupDetailsAsync");
        Assert.Equal("First talk - Alex Example", meetup.Title);
        Assert.Contains("# First talk - Alex Example", meetup.Description);
        session.TalkDescription = "About the talk.";
        await Call(component, "RefreshMeetupDetailsAsync");
        Assert.Contains("About the talk.", meetup.Description);
        session.TalkDescription = "**Updated abstract.**";
        await Call(component, "RefreshMeetupDetailsAsync");
        Assert.Contains("**Updated abstract.**", meetup.Description);
        Assert.DoesNotContain("About the talk.", meetup.Description);
        meetup.Description = "My custom agenda";
        session.TalkDescription = "A later change.";
        await Call(component, "RefreshMeetupDetailsAsync");
        Assert.Equal("My custom agenda", meetup.Description);
        await Call(component, "OnNewTalk", session);
        Assert.Equal("My custom agenda", meetup.Description);
    }

    [Fact]
    public async Task SecondSpeakerAndRemoval_UpdateGeneratedDescriptionAndGroupChangeUsesItsTemplate()
    {
        var component = await CreateAsync();
        var meetup = Field<MeetupEvent>(component, "evt");
        var speakers = Field<List<Speaker>>(component, "speakers");
        Field<EventSpeakerSession>(component, "session1").TalkTitle = "First talk";
        await Call(component, "OnSpeakerChangedAsync", 0, speakers[0].Id);
        await Call(component, "SetSpeakerCount", 2);
        Field<EventSpeakerSession>(component, "session2").TalkTitle = "Second talk";
        await Call(component, "OnSpeakerChangedAsync", 1, speakers[1].Id);
        Assert.Contains("Sam Example", meetup.Description);
        Assert.Contains("Our Speakers:", meetup.Description);
        await Call(component, "SetSpeakerCount", 1);
        Assert.DoesNotContain("Sam Example", meetup.Description);
        Assert.Contains("Our Speaker:", meetup.Description);
        var group = new MeetupGroup { Name = "Other group", MeetupDescriptionTemplate = "Group: {{GroupName}}\n{{Talks}}" };
        Field<List<MeetupGroup>>(component, "groups").Add(group);
        meetup.MeetupGroupId = group.Id;
        await Call(component, "RefreshDescriptionAsync");
        Assert.StartsWith("Group: Other group", meetup.Description);
    }

    [Fact]
    public async Task SavedCustomDescription_IsNotOverwrittenByTalkSelection()
    {
        var component = await CreateAsync();
        var meetup = Field<MeetupEvent>(component, "evt");
        meetup.Description = "# My saved event\n\nHand-written agenda";
        var speaker = Field<List<Speaker>>(component, "speakers")[0];
        await Call(component, "OnSpeakerChangedAsync", 0, speaker.Id);
        Field<EventSpeakerSession>(component, "session1").TalkTitle = "Changed talk";
        await Call(component, "RefreshMeetupDetailsAsync");
        Assert.Equal("# My saved event\n\nHand-written agenda", meetup.Description);
    }

    private static async Task<EventEdit> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new AppDbContext(options)) await db.Database.EnsureCreatedAsync();
        var component = new EventEdit();
        SetProperty(component, "DbContextFactory", new BookingContextFactory(options));
        SetProperty(component, "MeetupService", new MeetupService(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<MeetupService>.Instance));
        SetProperty(component, "SettingsService", DispatchProxy.Create<ISettingsService, SettingsStub>());
        SetProperty(component, "Logger", NullLogger<EventEdit>.Instance);
        var group = new MeetupGroup { Name = "Test group" };
        SetField(component, "groups", new List<MeetupGroup> { group });
        SetField(component, "speakers", new List<Speaker> { new() { Name = "Alex Example", Bio = "Alex builds apps." }, new() { Name = "Sam Example" } });
        SetField(component, "evt", new MeetupEvent { MeetupGroupId = group.Id, Title = "", Description = "" });
        return component;
    }

    private static Task Call(EventEdit component, string name, params object?[] args) =>
        (Task)typeof(EventEdit).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args)!;
    private static T Field<T>(EventEdit component, string name) =>
        (T)typeof(EventEdit).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
    private static void SetField(EventEdit component, string name, object value) =>
        typeof(EventEdit).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
    private static void SetProperty(EventEdit component, string name, object value) =>
        typeof(EventEdit).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    public class SettingsStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "GetSettingAsync" ? Task.FromResult<string?>(null) : throw new NotSupportedException();
    }
}
