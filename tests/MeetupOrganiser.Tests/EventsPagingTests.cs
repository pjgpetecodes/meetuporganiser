using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Web.Components.Pages;

namespace MeetupOrganiser.Tests;

public class EventsPagingTests
{
    [Fact]
    public void Events_ArePagedAndClampedAfterDeletion()
    {
        var events = CreateEvents(25);
        Assert.Equal(3, Property<int>(events, "PageCount"));
        Assert.Equal(12, Property<IEnumerable<MeetupEvent>>(events, "PagedEvents").Count());
        SetField(events, "page", 3);
        Assert.Equal("Event 25", Assert.Single(Property<IEnumerable<MeetupEvent>>(events, "PagedEvents")).Title);

        var remaining = Field<List<MeetupEvent>>(events, "allEvents");
        remaining.RemoveAt(24);

        Assert.Equal(2, Property<int>(events, "CurrentPage"));
        var results = Property<IEnumerable<MeetupEvent>>(events, "PagedEvents").ToList();
        Assert.Equal(12, results.Count);
        Assert.Equal("Event 13", results[0].Title);
        Assert.Equal("Event 24", results[^1].Title);
    }

    [Theory]
    [InlineData("searchQuery", "Event 2", 10)]
    [InlineData("selectedStatus", "Published", 15)]
    [InlineData("searchQuery", "Ada", 1)]
    public void Filtering_ResetsPageAndPagesOnlyMatches(string field, string value, int expectedCount)
    {
        var events = CreateEvents(30);
        SetField(events, "page", 3);
        SetField(events, field, value);
        typeof(Events).GetMethod("ResetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(events, null);

        Assert.Equal(1, Property<int>(events, "CurrentPage"));
        Assert.Equal(expectedCount, Property<int>(events, "EventCount"));
        Assert.Equal(Math.Min(12, expectedCount), Property<IEnumerable<MeetupEvent>>(events, "PagedEvents").Count());
    }

    [Fact]
    public void GroupAndStatusFilters_AreAppliedBeforePaging()
    {
        var events = CreateEvents(30);
        var groupId = Guid.NewGuid();
        var all = Field<List<MeetupEvent>>(events, "allEvents");
        all[0].MeetupGroupId = groupId;
        all[1].MeetupGroupId = groupId;
        SetField(events, "selectedGroupId", groupId.ToString());
        SetField(events, "selectedStatus", "Published");

        Assert.Equal(all[1], Assert.Single(Property<IEnumerable<MeetupEvent>>(events, "PagedEvents")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void EmptyOrSinglePageResults_DoNotNeedAdditionalPages(int count)
    {
        var events = CreateEvents(count);
        Assert.Equal(1, Property<int>(events, "PageCount"));
        Assert.Equal(count, Property<IEnumerable<MeetupEvent>>(events, "PagedEvents").Count());
    }

    private static Events CreateEvents(int count)
    {
        var events = new Events();
        var items = Enumerable.Range(1, count).Select(i => new MeetupEvent
        {
            Title = $"Event {i:00}",
            Status = i % 2 == 0 ? EventStatus.Published : EventStatus.Scheduled,
            Sessions = i == 1 ? [new EventSpeakerSession { Speaker = new Speaker { Name = "Ada" } }] : []
        }).ToList();
        SetField(events, "allEvents", items);
        return events;
    }

    [Fact]
    public void SwitchingViews_SeparatesDraftsFromUpcomingAndResetsPagingAndStatus()
    {
        var events = CreateEvents(30);
        var items = Field<List<MeetupEvent>>(events, "allEvents");
        items[0].Status = EventStatus.Draft;
        items[1].Status = EventStatus.Cancelled;
        Assert.Equal(29, Property<int>(events, "EventCount"));
        Assert.Contains(items[1], Property<IEnumerable<MeetupEvent>>(events, "filteredEvents"));
        SetField(events, "page", 3);
        SetField(events, "selectedStatus", "Published");

        SetView(events, "Drafts");

        Assert.Equal(1, Property<int>(events, "CurrentPage"));
        Assert.Equal(string.Empty, Field<string>(events, "selectedStatus"));
        Assert.Equal(items[0], Assert.Single(Property<IEnumerable<MeetupEvent>>(events, "PagedEvents")));
    }

    [Fact]
    public void UpcomingAndPast_UseEndTimeAndExcludeDrafts()
    {
        var events = CreateEvents(5);
        var items = Field<List<MeetupEvent>>(events, "allEvents");
        items[0].EventDateTimeUtc = DateTime.UtcNow.AddDays(-1);
        items[1].EventDateTimeUtc = DateTime.UtcNow.AddMinutes(-30);
        items[1].DurationMinutes = 90;
        items[3].Status = EventStatus.Draft;
        items[4].Status = EventStatus.Draft;
        items[4].EventDateTimeUtc = DateTime.UtcNow.AddDays(-1);

        Assert.Equal(new[] { items[1], items[2] }, Property<IEnumerable<MeetupEvent>>(events, "PagedEvents"));

        SetField(events, "page", 3);
        SetField(events, "selectedStatus", "Published");
        SetView(events, "Past");
        Assert.Equal(1, Property<int>(events, "CurrentPage"));
        Assert.Equal(string.Empty, Field<string>(events, "selectedStatus"));
        Assert.Equal(items[0], Assert.Single(Property<IEnumerable<MeetupEvent>>(events, "PagedEvents")));

        SetView(events, "Drafts");
        Assert.Equal(new[] { items[4], items[3] }, Property<IEnumerable<MeetupEvent>>(events, "PagedEvents"));
    }

    [Fact]
    public void Drafts_AreSortedEarliestFirstBeforePaging()
    {
        var events = CreateEvents(25);
        var items = Field<List<MeetupEvent>>(events, "allEvents");
        var start = DateTime.UtcNow.AddDays(1);
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Status = EventStatus.Draft;
            items[i].EventDateTimeUtc = start.AddDays(items.Count - i);
        }
        SetView(events, "Drafts");

        Assert.Equal(items.AsEnumerable().Reverse().Take(12), Property<IEnumerable<MeetupEvent>>(events, "PagedEvents"));
        SetField(events, "page", 2);
        Assert.Equal(items.AsEnumerable().Reverse().Skip(12).Take(12), Property<IEnumerable<MeetupEvent>>(events, "PagedEvents"));
        SetField(events, "page", 3);
        Assert.Equal(items[0], Assert.Single(Property<IEnumerable<MeetupEvent>>(events, "PagedEvents")));
    }

    private static void SetView(Events events, string view)
    {
        var method = typeof(Events).GetMethod("SetView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(events, [Enum.Parse(method.GetParameters()[0].ParameterType, view)]);
    }

    private static void SetField(object component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static T Field<T>(object component, string name) =>
        (T)component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private static T Property<T>(object component, string name) =>
        (T)component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
}
