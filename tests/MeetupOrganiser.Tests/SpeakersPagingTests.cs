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

public class SpeakersPagingTests
{
    [Fact]
    public async Task PagingControls_RenderOnceAfterAllSpeakerCards()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton(DispatchProxy.Create<IDbContextFactory<AppDbContext>, UnusedService>())
            .AddSingleton(DispatchProxy.Create<IBlobStorageService, UnusedService>())
            .AddSingleton(DispatchProxy.Create<ISessionizeService, UnusedService>())
            .AddSingleton(DispatchProxy.Create<ISettingsService, UnusedService>())
            .AddSingleton(DispatchProxy.Create<IJSRuntime, UnusedService>())
            .AddSingleton<NavigationManager, TestNavigationManager>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<LoadedSpeakers>();
            return output.ToHtmlString();
        });

        var navIndex = html.IndexOf("<nav ", StringComparison.Ordinal);
        Assert.True(navIndex > html.LastIndexOf("</a>", StringComparison.Ordinal));
        Assert.Equal(1, html.Split("aria-label=\"Speaker directory pages\"").Length - 1);
        Assert.Contains("Page 1 of 2", html);
        Assert.DoesNotContain("Person 13", html);
        Assert.DoesNotContain("<nav ", html[..navIndex]);
    }

    [Fact]
    public void Directory_PagesCardsAndClampsPageWhenResultsShrink()
    {
        var directory = CreateDirectory(25);
        Assert.Equal(3, Property<int>(directory, "PageCount"));
        Assert.Equal(12, Property<IEnumerable<Speaker>>(directory, "PagedSpeakers").Count());
        SetField(directory, "page", 3);
        Assert.Equal("Person 25", Assert.Single(Property<IEnumerable<Speaker>>(directory, "PagedSpeakers")).Name);

        SetField(directory, "allSpeakers", Speakers(24));

        Assert.Equal(2, Property<int>(directory, "CurrentPage"));
        var results = Property<IEnumerable<Speaker>>(directory, "PagedSpeakers").ToList();
        Assert.Equal(12, results.Count);
        Assert.Equal("Person 13", results[0].Name);
        Assert.Equal("Person 24", results[^1].Name);
    }

    [Fact]
    public void Searching_ResetsPageAndPaginatesOnlyMatches()
    {
        var directory = CreateDirectory(30);
        SetField(directory, "page", 3);

        typeof(Speakers).GetProperty("SearchQuery", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(directory, "Person 2");

        Assert.Equal(1, Property<int>(directory, "CurrentPage"));
        Assert.Equal(10, Property<int>(directory, "SpeakerCount"));
        Assert.Equal(1, Property<int>(directory, "PageCount"));
        Assert.All(Property<IEnumerable<Speaker>>(directory, "PagedSpeakers"), speaker => Assert.StartsWith("Person 2", speaker.Name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void EmptyOrSinglePageDirectory_DoesNotNeedAdditionalPages(int count)
    {
        var directory = CreateDirectory(count);

        Assert.Equal(1, Property<int>(directory, "PageCount"));
        Assert.Equal(count, Property<int>(directory, "SpeakerCount"));
        Assert.Equal(count, Property<IEnumerable<Speaker>>(directory, "PagedSpeakers").Count());
    }

    private static Speakers CreateDirectory(int count)
    {
        var directory = new Speakers();
        SetField(directory, "allSpeakers", Speakers(count));
        return directory;
    }

    private static List<Speaker> Speakers(int count) =>
        Enumerable.Range(1, count).Select(i => new Speaker { Name = $"Person {i:00}" }).ToList();

    private static void SetField(object component, string name, object value) =>
        typeof(Speakers).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static T Property<T>(object component, string name) =>
        (T)component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    public sealed class LoadedSpeakers : Speakers
    {
        protected override Task OnInitializedAsync()
        {
            SetField(this, "allSpeakers", SpeakersPagingTests.Speakers(13));
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
        public TestNavigationManager() => Initialize("https://meetup.example.com/", "https://meetup.example.com/speakers");
    }
}
