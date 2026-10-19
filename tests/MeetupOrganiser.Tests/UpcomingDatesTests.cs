using System.Reflection;
using System.Text.RegularExpressions;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web;

namespace MeetupOrganiser.Tests;

public class UpcomingDatesTests
{
    [Fact]
    public async Task HolidayCalendar_AnnotatesSelectedListAndDoesNotBlockSavingAnEmptyDraft()
    {
        var factory = new CalendarDbFactory();
        var group = new MeetupGroup { Name = "Calendar group" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(group);
            await db.SaveChangesAsync();
        }
        var holiday = new DateOnly(2032, 2, 9);
        var stub = new BankHolidayStub(new BankHolidayCalendar([new(holiday, "Test public holiday")]));
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(stub)
            .AddSingleton<NavigationManager, CalendarNavigationManager>()
            .AddSingleton<IDbContextFactory<AppDbContext>>(factory)
            .AddScoped<UpcomingMeetupScheduler>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SelectedHolidayCalendar>();
            return output.ToHtmlString();
        });
        var button = Regex.Match(html, "<button[^>]*aria-label=\"Monday 9 February 2032\"[^>]*>.*?</button>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(button);
        Assert.Contains("calendar-day public-holiday selected", button);
        Assert.Contains("aria-pressed=\"true\"", button);
        Assert.DoesNotContain("disabled", button);
        Assert.Contains("public-holiday-badge", button);
        var selectedList = html[html.IndexOf("Selected dates (1)", StringComparison.Ordinal)..];
        Assert.Contains("Public holiday: Test public holiday", selectedList);
        Assert.Equal(1, stub.Calls);

        var calendar = new UpcomingDates();
        SetProperty(calendar, "DbContextFactory", factory);
        SetProperty(calendar, "Scheduler", new UpcomingMeetupScheduler(factory));
        SetProperty(calendar, "Logger", NullLogger<UpcomingDates>.Instance);
        SetField(calendar, "loadedGroupId", group.Id);
        Invoke(calendar, "ToggleDate", holiday);
        Assert.Contains(holiday, GetField<HashSet<DateOnly>>(calendar, "selectedDates"));
        await InvokeAsync(calendar, "CreateDraftsAsync");
        Assert.False(GetField<bool>(calendar, "isError"));
        await using var check = factory.CreateDbContext();
        Assert.Single(await check.MeetupEvents.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Calendar_RendersMondayToSundayMonthGridAndExistingMeetups(bool returnToCfp)
    {
        var factory = new CalendarDbFactory();
        var group = new MeetupGroup { Name = "Calendar group", DefaultLocation = "Group venue" };
        var existing = new MeetupEvent { MeetupGroup = group, Title = "Existing Meetup", EventDateTimeUtc = new(2032, 2, 10, 18, 30, 0, DateTimeKind.Utc) };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(existing);
            await db.SaveChangesAsync();
        }
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub())
            .AddSingleton<NavigationManager, CalendarNavigationManager>()
            .AddSingleton<IDbContextFactory<AppDbContext>>(factory)
            .AddScoped<UpcomingMeetupScheduler>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = returnToCfp
                ? await renderer.RenderComponentAsync<CfpFebruaryCalendar>()
                : await renderer.RenderComponentAsync<FebruaryCalendar>();
            return output.ToHtmlString();
        });
        Assert.Contains("February 2032 (UK dates)", html);
        var weekdays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (var i = 0; i < weekdays.Length; i++)
        {
            Assert.Matches($"<th\\b[^>]*scope=\"col\"[^>]*>{weekdays[i]}</th>", html);
            if (i > 0) Assert.True(html.IndexOf($">{weekdays[i - 1]}</th>", StringComparison.Ordinal) < html.IndexOf($">{weekdays[i]}</th>", StringComparison.Ordinal));
        }
        Assert.Equal(29, html.Split("class=\"calendar-day").Length - 1);
        var firstRow = Regex.Match(html, @"<tbody[^>]*>\s*<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(6, Regex.Matches(firstRow, @"<td[^>]*>\s*</td>").Count);
        Assert.Contains("Sunday 1 February 2032", firstRow);
        Assert.Matches("<button[^>]*aria-label=\"Tuesday 10 February 2032\"[^>]*disabled", html);
        Assert.Contains($"href=\"/events/edit/{existing.Id}\"", html);
        Assert.Contains("Existing Meetup", html);
        Assert.Contains("Save dates", html);
        Assert.Contains("no speakers or talks", html);
        Assert.DoesNotContain("Shared meetup details", html);
        Assert.DoesNotContain("schedule-time", html);
        Assert.DoesNotContain("schedule-format", html);
        Assert.Contains("Previous month", html);
        Assert.Contains("Next month", html);
        Assert.Contains("id=\"calendar-year\"", html);
        Assert.Contains(returnToCfp ? $"href=\"/groups/edit/{group.Id}?tab=cfp\"" : "href=\"/events\"", html);
        Assert.Contains(returnToCfp ? ">Back to CFP</a>" : ">Back to Events</a>", html);
        if (returnToCfp) Assert.DoesNotContain("Back to Events", html);
    }

    [Fact]
    public void SelectingDates_PersistsAcrossMonthsAndYearsAndBlocksExistingDays()
    {
        var calendar = new UpcomingDates();
        SetField(calendar, "calendarMonth", new DateOnly(2032, 12, 1));
        var december = new DateOnly(2032, 12, 15);
        var january = new DateOnly(2033, 1, 20);
        SetField(calendar, "existingEvents", new List<MeetupEvent>
        {
            new() { EventDateTimeUtc = new(2033, 1, 19, 23, 30, 0, DateTimeKind.Utc) }
        });
        Invoke(calendar, "ToggleDate", december);
        Invoke(calendar, "MoveMonth", 1);
        Assert.Equal(new DateOnly(2033, 1, 1), GetField<DateOnly>(calendar, "calendarMonth"));
        Invoke(calendar, "ToggleDate", january);
        Assert.Equal(new[] { december, january }, GetField<HashSet<DateOnly>>(calendar, "selectedDates").Order());
        Invoke(calendar, "ToggleDate", new DateOnly(2033, 1, 19));
        Assert.Equal(2, GetField<HashSet<DateOnly>>(calendar, "selectedDates").Count);
        Invoke(calendar, "ToggleDate", new DateOnly(2000, 1, 1));
        Assert.Equal(2, GetField<HashSet<DateOnly>>(calendar, "selectedDates").Count);
        Invoke(calendar, "ChangeYear", new ChangeEventArgs { Value = "2036" });
        Invoke(calendar, "ChangeMonth", new ChangeEventArgs { Value = "2" });
        Assert.Equal(new DateOnly(2036, 2, 1), GetField<DateOnly>(calendar, "calendarMonth"));
        Invoke(calendar, "ToggleDate", december);
        Assert.Equal(january, Assert.Single(GetField<HashSet<DateOnly>>(calendar, "selectedDates")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatingDrafts_PreservesSelectionOnConflictAndClearsItAfterSuccess(bool returnToCfp)
    {
        var factory = new CalendarDbFactory();
        var group = new MeetupGroup { Name = "Calendar group" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(new MeetupEvent { MeetupGroup = group, EventDateTimeUtc = new(2032, 2, 10, 18, 30, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }
        var calendar = new UpcomingDates();
        SetProperty(calendar, "DbContextFactory", factory);
        SetProperty(calendar, "Scheduler", new UpcomingMeetupScheduler(factory));
        SetProperty(calendar, "Logger", NullLogger<UpcomingDates>.Instance);
        var navigation = new CalendarNavigationManager();
        SetProperty(calendar, "Navigation", navigation);
        SetProperty(calendar, "GroupId", group.Id);
        SetProperty(calendar, "ReturnToCfp", returnToCfp);
        SetField(calendar, "loadedGroupId", group.Id);
        var dates = GetField<HashSet<DateOnly>>(calendar, "selectedDates");
        dates.Add(new(2032, 2, 10));
        dates.Add(new(2032, 2, 11));

        await InvokeAsync(calendar, "CreateDraftsAsync");
        Assert.True(GetField<bool>(calendar, "isError"));
        Assert.Equal(2, dates.Count);
        Assert.Single(GetField<List<MeetupEvent>>(calendar, "existingEvents"));
        Assert.EndsWith("/events/upcoming", navigation.Uri);
        dates.Remove(new(2032, 2, 10));

        await InvokeAsync(calendar, "CreateDraftsAsync");
        Assert.False(GetField<bool>(calendar, "isError"));
        Assert.Empty(dates);
        Assert.Equal(2, GetField<List<MeetupEvent>>(calendar, "existingEvents").Count);
        Assert.Contains("Created 1 draft", GetField<string>(calendar, "message"));
        await using var check = factory.CreateDbContext();
        var draft = await check.MeetupEvents.Include(e => e.Sessions).SingleAsync(e => e.EventDateTimeUtc.Day == 11);
        Assert.Equal(EventStatus.Draft, draft.Status);
        Assert.Empty(draft.Sessions);
        Assert.Null(draft.Description);
        Assert.EndsWith(returnToCfp ? $"/groups/edit/{group.Id}?tab=cfp" : "/events/upcoming", navigation.Uri);
    }

    [Fact]
    public async Task ChangingGroup_ClearsSelectionAndLoadsOnlyThatGroupsCalendarAndLocation()
    {
        var factory = new CalendarDbFactory();
        var first = new MeetupGroup { Name = "First group" };
        var second = new MeetupGroup { Name = "Second group", DefaultLocation = "Second venue" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(new MeetupEvent { MeetupGroup = first });
            db.Add(new MeetupEvent { MeetupGroup = second });
            await db.SaveChangesAsync();
        }
        var calendar = new UpcomingDates();
        SetProperty(calendar, "DbContextFactory", factory);
        SetProperty(calendar, "Logger", NullLogger<UpcomingDates>.Instance);
        SetField(calendar, "groups", new List<MeetupGroup> { first, second });
        SetField(calendar, "selectedGroupId", second.Id.ToString());
        GetField<HashSet<DateOnly>>(calendar, "selectedDates").Add(new(2032, 2, 11));
        await InvokeAsync(calendar, "LoadGroupAsync");
        Assert.Empty(GetField<HashSet<DateOnly>>(calendar, "selectedDates"));
        Assert.Equal(second.Id, GetField<Guid?>(calendar, "loadedGroupId"));
        Assert.Equal(second.Id, Assert.Single(GetField<List<MeetupEvent>>(calendar, "existingEvents")).MeetupGroupId);
        Assert.Equal("Second venue", GetField<string>(calendar, "location"));
    }

    public sealed class FebruaryCalendar : UpcomingDates
    {
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            SetField(this, "calendarMonth", new DateOnly(2032, 2, 1));
        }
    }

    public sealed class CfpFebruaryCalendar : UpcomingDates
    {
        public CfpFebruaryCalendar() => ReturnToCfp = true;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            SetField(this, "calendarMonth", new DateOnly(2032, 2, 1));
        }
    }

    private sealed class CalendarNavigationManager : NavigationManager
    {
        public CalendarNavigationManager() => Initialize("https://meetup.example.com/", "https://meetup.example.com/events/upcoming");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    public sealed class SelectedHolidayCalendar : UpcomingDates
    {
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            SetField(this, "calendarMonth", new DateOnly(2032, 2, 1));
            GetField<HashSet<DateOnly>>(this, "selectedDates").Add(new(2032, 2, 9));
        }
    }

    private static void Invoke(UpcomingDates calendar, string method, params object[] args) =>
        typeof(UpcomingDates).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(calendar, args);
    private static async Task InvokeAsync(UpcomingDates calendar, string method) =>
        await (Task)typeof(UpcomingDates).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(calendar, null)!;
    private static void SetProperty(UpcomingDates calendar, string name, object value) =>
        typeof(UpcomingDates).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(calendar, value);
    private static void SetField(UpcomingDates calendar, string name, object value) =>
        typeof(UpcomingDates).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(calendar, value);
    private static T GetField<T>(UpcomingDates calendar, string name) =>
        (T)typeof(UpcomingDates).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(calendar)!;
}
