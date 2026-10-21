using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace MeetupOrganiser.Tests;

public class GroupEditRenderingTests
{
    [Theory]
    [InlineData("https://example.com/group-logo.png", true)]
    [InlineData(null, false)]
    public async Task Editor_ShowsGroupIdentityAboveTabs(string? logoUrl, bool cfpTab)
    {
        using var services = new ServiceCollection().AddLogging()
            .AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .AddScoped<ITemplateSectionService, TemplateSectionService>()
            .AddSingleton(DispatchProxy.Create<IPptxPreviewService, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton(DispatchProxy.Create<IBlobStorageService, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton(DispatchProxy.Create<IJSRuntime, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton<NavigationManager, TestNavigationManager>()
            .BuildServiceProvider();
        var group = new MeetupGroup { Name = "Example Meetup", LogoBlobUrl = logoUrl };
        await using (var db = services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            db.MeetupGroups.Add(group);
            await db.SaveChangesAsync();
        }
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(GroupEdit.Id)] = group.Id });
            var output = cfpTab
                ? await renderer.RenderComponentAsync<CfpGroupEdit>(parameters)
                : await renderer.RenderComponentAsync<GroupEdit>(parameters);
            return output.ToHtmlString();
        });

        var heading = html[..html.IndexOf("page-tabs", StringComparison.Ordinal)];
        Assert.Contains(">Example Meetup</h1>", heading);
        Assert.Contains("Edit Meetup Group", heading);
        var tabs = html[html.IndexOf("page-tabs", StringComparison.Ordinal)..];
        tabs = tabs[..tabs.IndexOf("</ul>", StringComparison.Ordinal)];
        Assert.True(tabs.IndexOf("Custom PPTX Template", StringComparison.Ordinal) <
                    tabs.IndexOf("Call for Speakers", StringComparison.Ordinal));
        Assert.Equal(cfpTab, tabs.Contains("class=\"nav-link active\">Call for Speakers", StringComparison.Ordinal));
        Assert.Contains($"/events/upcoming?groupId={group.Id}&amp;returnToCfp=true", html);
        if (logoUrl != null)
        {
            Assert.Contains($"src=\"{logoUrl}\"", heading);
            Assert.Contains("alt=\"Example Meetup logo\"", heading);
        }
        else
        {
            Assert.DoesNotContain("<img ", heading);
        }
    }

    public sealed class CfpGroupEdit : GroupEdit
    {
        public CfpGroupEdit() => Tab = "cfp";
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://meetup.example.com/", "https://meetup.example.com/groups/");
    }
}
