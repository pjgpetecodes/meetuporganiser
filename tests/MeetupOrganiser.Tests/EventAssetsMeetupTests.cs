using System.Reflection;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Web.Components;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class EventAssetsMeetupTests
{
    [Fact]
    public async Task WebsitePreparation_TransfersEditedPreviewAndReportsNoPublication()
    {
        var component = CreateComponent(new MeetupStub(null));
        SetField(component, "selectedEvent", new MeetupEvent
        {
            MeetupGroup = new() { MeetupUrlName = "test-group" },
            EventDateTimeUtc = new(2027, 1, 25, 18, 30, 0, DateTimeKind.Utc)
        });
        SetField(component, "meetupTitle", "Edited preview title");
        SetField(component, "meetupMarkdownDescription", "Edited preview description");
        SetField(component, "previewImageBase64", Convert.ToBase64String([137, 80, 78, 71, 13, 10, 26, 10, 0]));

        await (Task)typeof(EventAssets).GetMethod("PrepareWebsiteHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;

        var payload = System.Text.Json.JsonSerializer.Deserialize<MeetupWebsiteHandoff>(
            Field<string>(component, "websiteHandoffJson"), System.Text.Json.JsonSerializerOptions.Web)!;
        Assert.Equal("Edited preview title", payload.Title);
        Assert.Equal("Edited preview description", payload.Description);
        Assert.Contains("No event has been saved or published", Field<string>(component, "statusMessage"));
        Assert.False(Field<bool>(component, "isError"));
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Published)]
    public async Task WebsiteCompletion_PersistsOnlyExplicitlyConfirmedOutcome(EventStatus status)
    {
        var fixture = await BookingFixture.CreateAsync();
        var meetup = await fixture.AddDateAsync();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.MeetupGroups.SingleAsync()).MeetupUrlName = "test-group";
            await db.SaveChangesAsync();
        }
        var component = CreateComponent(new MeetupStub(null));
        component.EventId = meetup.Id;
        typeof(EventAssets).GetProperty("DbContextFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, fixture.Factory);
        SetField(component, "websiteCompletedUrl", "https://www.meetup.com/test-group/events/123/");
        SetField(component, "websiteCompletedStatus", status);
        async Task Record() => await (Task)typeof(EventAssets)
            .GetMethod("RecordWebsiteOutcomeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;

        await Record();
        await using (var db = fixture.Factory.CreateDbContext())
            Assert.Null((await db.MeetupEvents.SingleAsync()).MeetupEventUrl);
        SetField(component, "websiteOutcomeConfirmed", true);
        await Record();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var stored = await db.MeetupEvents.SingleAsync();
            Assert.Equal("123", stored.MeetupEventId);
            Assert.Equal("https://www.meetup.com/test-group/events/123/", stored.MeetupEventUrl);
            Assert.Equal(status, stored.Status);
        }
        Assert.Contains("reported by you", Field<string>(component, "statusMessage"));
        Assert.False(Field<bool>(component, "websiteOutcomeConfirmed"));
        Assert.False(Field<bool>(component, "recordingWebsiteOutcome"));
    }

    [Fact]
    public async Task WebsiteCompletion_WrongGroupDoesNotChangeEvent()
    {
        var fixture = await BookingFixture.CreateAsync();
        var meetup = await fixture.AddDateAsync();
        var component = CreateComponent(new MeetupStub(null));
        component.EventId = meetup.Id;
        typeof(EventAssets).GetProperty("DbContextFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, fixture.Factory);
        SetField(component, "websiteCompletedUrl", "https://www.meetup.com/wrong-group/events/123/");
        SetField(component, "websiteOutcomeConfirmed", true);
        await (Task)typeof(EventAssets).GetMethod("RecordWebsiteOutcomeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;
        Assert.True(Field<bool>(component, "isError"));
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Null((await db.MeetupEvents.SingleAsync()).MeetupEventId);
    }

    private static void SetField(EventAssets component, string name, object value) =>
        typeof(EventAssets).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Publication_DistinguishesManualHandoffFromSuccessAndFailure(bool manual, bool success)
    {
        var result = new MeetupCreationResult(success, success ? "123" : null,
            "https://www.meetup.com/test-group/", success || manual ? null : "Meetup rejected this event.")
        {
            RequiresManualAction = manual
        };
        var component = CreateComponent(new MeetupStub(result));

        await PublishAsync(component);

        var message = Field<string>(component, "statusMessage");
        Assert.Equal(!manual && !success, Field<bool>(component, "isError"));
        Assert.Equal(manual ? result.EventUrl : null, Field<string?>(component, "meetupManualUrl"));
        if (manual)
            Assert.Contains("No event has been published", message);
        else if (success)
            Assert.Contains("Published to Meetup!", message);
        else
            Assert.Contains(result.ErrorMessage!, message);
        Assert.False(Field<bool>(component, "isPublishingMeetup"));
    }

    [Fact]
    public async Task Publication_ExceptionDoesNotClaimSuccessAndWarnsAgainstDuplicateRetry()
    {
        var component = CreateComponent(new MeetupStub(null));

        await PublishAsync(component);

        Assert.True(Field<bool>(component, "isError"));
        Assert.Contains("Check your Meetup group", Field<string>(component, "statusMessage"));
        Assert.Null(Field<string?>(component, "meetupManualUrl"));
        Assert.False(Field<bool>(component, "isPublishingMeetup"));
    }

    private static EventAssets CreateComponent(IMeetupService service)
    {
        var component = new EventAssets();
        typeof(EventAssets).GetProperty("MeetupService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, service);
        typeof(EventAssets).GetProperty("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, NullLogger<EventAssets>.Instance);
        typeof(EventAssets).GetField("selectedEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, new MeetupEvent());
        return component;
    }

    private static Task PublishAsync(EventAssets component) =>
        (Task)typeof(EventAssets).GetMethod("PublishToMeetupAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;

    private static T Field<T>(EventAssets component, string name) =>
        (T)typeof(EventAssets).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private sealed class MeetupStub(MeetupCreationResult? result) : IMeetupService
    {
        public Task<MeetupCreationResult> CreateEventAsync(MeetupEvent meetupEvent, CancellationToken cancellationToken = default) =>
            result is null ? throw new HttpRequestException("Connection failed") : Task.FromResult(result);
        public Task<MeetupEventImportResult> ImportPastEventsAsync(string urlname, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int?> GetAttendeeCountAsync(string eventUrl, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public string GenerateMeetupDescription(MeetupEvent meetupEvent, string? bioPreference = null, string? template = null,
            IReadOnlyList<TemplateSection>? sections = null) =>
            throw new NotSupportedException();
    }
}
