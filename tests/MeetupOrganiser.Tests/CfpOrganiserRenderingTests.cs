using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace MeetupOrganiser.Tests;

public class CfpOrganiserRenderingTests
{
    [Fact]
    public async Task LegacyUpcomingDateUrl_RedirectsToCalendarWithoutLoadingEventEditor()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<LegacyUpcomingEventEdit>();
            return output.ToHtmlString();
        });
        Assert.EndsWith("/events/upcoming", services.GetRequiredService<NavigationManager>().Uri);
        Assert.DoesNotContain("Select Speaker", html);
        Assert.DoesNotContain("Event Title", html);
        Assert.DoesNotContain("event editor could not be loaded", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Editor_ShowsBookingControlsOnlyAfterLoadingAnEvent(bool isNew)
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<LoadedEventEdit>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(EventEdit.Id)] = isNew ? null : Guid.NewGuid()
            }));
            return output.ToHtmlString();
        });

        Assert.Contains("Open this draft date for speaker bookings", html);
        Assert.Contains("Date &amp; Start Time (UK)", html);
        Assert.Contains("Calendar month", html);
        Assert.Contains(">Mon</th>", html);
        Assert.Contains(">Sun</th>", html);
        Assert.DoesNotContain("datetime-local", html);
        Assert.Equal(isNew, html.Contains("Let the speaker choose an upcoming date", StringComparison.Ordinal));
        Assert.DoesNotContain("event editor could not be loaded", html);
        Assert.DoesNotMatch(@">\s*}\s*<", html);
    }

    [Fact]
    public async Task DraftEvents_RenderOnePageLevelPagerOutsideCards()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<LoadedDraftEvents>();
            return output.ToHtmlString();
        });

        Assert.Contains(">Upcoming / Live Meetups</button>", html);
        Assert.Contains(">Past</button>", html);
        Assert.Contains(">Drafts</button>", html);
        Assert.True(html.IndexOf(">Upcoming / Live Meetups</button>", StringComparison.Ordinal) <
                    html.IndexOf(">Past</button>", StringComparison.Ordinal));
        Assert.True(html.IndexOf(">Past</button>", StringComparison.Ordinal) <
                    html.IndexOf(">Drafts</button>", StringComparison.Ordinal));
        Assert.Contains("Open for bookings", html);
        Assert.Contains("Page 1 of 2", html);
        Assert.Equal(1, html.Split("aria-label=\"Event list pages\"").Length - 1);
        Assert.True(html.IndexOf("<nav ", StringComparison.Ordinal) > html.LastIndexOf("</article>", StringComparison.Ordinal));
        Assert.DoesNotContain("Date 13", html);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging()
        .AddSingleton<IBankHolidayService>(new BankHolidayStub())
        .AddSingleton(DispatchProxy.Create<IDbContextFactory<AppDbContext>, UnusedService>())
        .AddSingleton(DispatchProxy.Create<IMagicLinkService, UnusedService>())
        .AddSingleton(DispatchProxy.Create<ISpeakerBookingService, UnusedService>())
        .AddSingleton(DispatchProxy.Create<IZoomService, UnusedService>())
        .AddSingleton(DispatchProxy.Create<ISessionizeService, UnusedService>())
        .AddSingleton(DispatchProxy.Create<IMeetupService, UnusedService>())
        .AddSingleton(DispatchProxy.Create<ISettingsService, UnusedService>())
        .AddSingleton(DispatchProxy.Create<IJSRuntime, UnusedService>())
        .AddSingleton<NavigationManager, TestNavigationManager>()
        .BuildServiceProvider();

    private static void SetField<T>(T component, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    public sealed class LegacyUpcomingEventEdit : EventEdit
    {
        public LegacyUpcomingEventEdit() => Upcoming = true;
    }

    public sealed class LoadedEventEdit : EventEdit
    {
        protected override Task OnInitializedAsync()
        {
            SetField<EventEdit>(this, "evt", new MeetupEvent { Title = "Upcoming date" });
            SetField<EventEdit>(this, "groups", new List<MeetupGroup>());
            SetField<EventEdit>(this, "speakers", new List<Speaker>());
            return Task.CompletedTask;
        }
    }

    public sealed class LoadedDraftEvents : Events
    {
        protected override Task OnInitializedAsync()
        {
            SetField<Events>(this, "allEvents", Enumerable.Range(1, 13).Select(i => new MeetupEvent
            {
                Title = $"Date {i:00}", Status = EventStatus.Draft, IsOpenForBookings = true
            }).ToList());
            var viewField = typeof(Events).GetField("selectedView", BindingFlags.Instance | BindingFlags.NonPublic)!;
            viewField.SetValue(this, Enum.Parse(viewField.FieldType, "Drafts"));
            return Task.CompletedTask;
        }
    }

    public class UnusedService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected service call: {targetMethod?.Name}");
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://meetup.example.com/", "https://meetup.example.com/events/new");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }
}
