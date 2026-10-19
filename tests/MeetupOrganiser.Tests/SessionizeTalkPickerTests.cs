using System.Reflection;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Tests;

public class SessionizeTalkPickerTests
{
    [Fact]
    public async Task ImportedProfile_ShowsItsTalksWithoutSearchingAndAllowsNewTalk()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton(DispatchProxy.Create<ISessionizeProfileService, CfpOrganiserRenderingTests.UnusedService>())
            .BuildServiceProvider();
        var talk = new SessionizeProfileSession("Building services", "A practical abstract", null);
        var profile = new SessionizeProfile("ada", "https://sessionize.com/ada", "Ada", null, null, null, null, [], [talk]);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SessionizeTalkPicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(SessionizeTalkPicker.Speaker)] = new Speaker { Name = "Ada" },
                    [nameof(SessionizeTalkPicker.Profile)] = profile
                }));
            return output.ToHtmlString();
        });
        Assert.Contains("Building services", html);
        Assert.Contains("Create a new talk", html);
        Assert.DoesNotContain("Could not reach Sessionize", html);

        SessionizeProfileSession? chosen = null;
        var newTalk = false;
        var picker = new SessionizeTalkPicker
        {
            Speaker = new Speaker { Name = "Ada" }, Profile = profile,
            OnPick = EventCallback.Factory.Create<SessionizeProfileSession>(new object(), value => chosen = value),
            OnNewTalk = EventCallback.Factory.Create(new object(), () => newTalk = true)
        };
        await InvokeAsync(picker, "OnParametersSetAsync");
        await InvokeAsync(picker, "OnPicked", new ChangeEventArgs { Value = "0" });
        Assert.Equal(talk, chosen);
        await InvokeAsync(picker, "OnPicked", new ChangeEventArgs { Value = "new" });
        Assert.True(newTalk);

        picker.Profile = profile with { Sessions = [new("Another profile's talk", null, null)] };
        await InvokeAsync(picker, "OnParametersSetAsync");
        await InvokeAsync(picker, "OnPicked", new ChangeEventArgs { Value = "0" });
        Assert.Equal("Another profile's talk", chosen?.Title);
    }

    private static async Task InvokeAsync(object component, string method, params object[] args) =>
        await (Task)component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args)!;
}
