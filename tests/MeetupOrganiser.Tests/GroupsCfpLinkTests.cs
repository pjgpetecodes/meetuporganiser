using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace MeetupOrganiser.Tests;

public class GroupsCfpLinkTests
{
    [Theory]
    [InlineData(true, "public-key", true)]
    [InlineData(false, "revoked-key", false)]
    [InlineData(true, null, false)]
    public async Task Cards_ShowCopyableLinkOnlyForEnabledGeneratedCfp(bool enabled, string? key, bool visible)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.MeetupGroups.Add(new MeetupGroup { Name = "Test group", CfpEnabled = enabled, CfpKey = key });
        await db.SaveChangesAsync();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton(db)
            .AddSingleton<NavigationManager, TestNavigationManager>()
            .AddSingleton<IJSRuntime>(new CopyRuntime(true)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<Groups>();
            return output.ToHtmlString();
        });
        Assert.Equal(visible, html.Contains("Public CFP link", StringComparison.Ordinal));
        if (visible)
        {
            Assert.Contains("value=\"https://example.com/meetup/cfp/public-key\"", html);
            Assert.Contains("readonly", html);
            Assert.Contains("Copy CFP link for Test group", html);
        }
        else if (key != null)
        {
            Assert.DoesNotContain(key, html);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Copy_UsesExistingClipboardHelperAndReportsFailure(bool copied, bool throws)
    {
        var page = new Groups();
        var js = new CopyRuntime(copied, throws);
        SetProperty(page, "Navigation", new TestNavigationManager());
        SetProperty(page, "JS", js);
        SetProperty(page, "Logger", NullLogger<Groups>.Instance);
        var group = new MeetupGroup { Name = "Test group", CfpEnabled = true, CfpKey = "public-key" };
        await (Task)typeof(Groups).GetMethod("CopyCfpLinkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [group])!;
        Assert.Equal("meetupOrganiser.copyText", js.Identifier);
        Assert.Equal("https://example.com/meetup/cfp/public-key", Assert.Single(js.Arguments!));
        Assert.Equal(!copied, typeof(Groups).GetField("copyFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        var message = (string)typeof(Groups).GetField("copyMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Assert.Contains(copied ? "copied" : "Ctrl+C", message);
    }

    private static void SetProperty(object component, string name, object value) =>
        component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://example.com/meetup/", "https://example.com/meetup/groups");
    }

    private sealed class CopyRuntime(bool copied, bool throws = false) : IJSRuntime
    {
        public string? Identifier { get; private set; }
        public object?[]? Arguments { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            if (throws) throw new JSException("Clipboard unavailable.");
            return ValueTask.FromResult((TValue)(object)copied);
        }
    }
}
