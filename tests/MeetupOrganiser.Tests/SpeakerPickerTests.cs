using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Tests;

public class SpeakerPickerTests
{
    [Theory]
    [InlineData(" ADA ", "all", "Ada")]
    [InlineData("cloud", "all", "Ada")]
    [InlineData("EXAMPLE.COM", "with-email", "Ada")]
    [InlineData("", "without-email", "Grace")]
    [InlineData("cloud", "without-email", null)]
    public void SearchAndEmailFilter_CombineAcrossNameBioAndEmail(string query, string filter, string? expectedName)
    {
        var picker = new SpeakerPicker();
        SetProperty(picker, nameof(SpeakerPicker.Speakers), new List<Speaker>
        {
            new() { Name = "Ada", Bio = "Cloud developer", Email = "ada@example.com" },
            new() { Name = "Grace", Email = " " }
        });
        SetProperty(picker, "SearchQuery", query);
        SetProperty(picker, "EmailFilter", filter);

        var results = (List<Speaker>)GetProperty(picker, "FilteredSpeakers")!;

        if (expectedName == null) Assert.Empty(results);
        else Assert.Equal(expectedName, Assert.Single(results).Name);
    }

    [Theory]
    [InlineData("SearchQuery", "Ada")]
    [InlineData("EmailFilter", "with-email")]
    public void ChangingSearchOrFilter_ResetsPagingAndPreservesSelection(string property, string value)
    {
        var picker = new SpeakerPicker();
        var selectedId = Guid.NewGuid();
        SetProperty(picker, nameof(SpeakerPicker.SelectedSpeakerId), selectedId);
        SetField(picker, "page", 3);

        SetProperty(picker, property, value);

        Assert.Equal(1, GetField(picker, "page"));
        Assert.Equal(selectedId, picker.SelectedSpeakerId);
    }

    [Theory]
    [InlineData(1, "Person 01", "Person 06", "Person 07", "Page 1 of 2")]
    [InlineData(2, "Person 07", "Person 08", "Person 01", "Page 2 of 2")]
    [InlineData(9, "Person 07", "Person 08", "Person 01", "Page 2 of 2")]
    public async Task Cards_ArePagedAndSelectionStaysVisible(int page, string first, string last, string hidden, string pageLabel)
    {
        var speakers = Enumerable.Range(1, 8).Select(i => new Speaker { Name = $"Person {i:00}" }).ToList();
        var html = await RenderAsync(speakers, speakers[7].Id, page);

        Assert.Contains($"aria-label=\"Select {first}\"", html);
        Assert.Contains($"aria-label=\"Select {last}\"", html);
        Assert.DoesNotContain($"aria-label=\"Select {hidden}\"", html);
        Assert.Contains(pageLabel, html);
        Assert.Contains("8 speaker(s) found", html);
        Assert.Contains("Selected speaker", html);
        Assert.Contains("Person 08", html);
    }

    [Fact]
    public async Task EmptyAndSmallDirectories_DoNotShowPaging()
    {
        var empty = await RenderAsync([], null);
        Assert.Contains("No speakers match", empty);
        Assert.DoesNotContain("Speaker results pages", empty);

        var small = await RenderAsync([new Speaker { Name = "Ada", Bio = "<script>alert(1)</script>" }], null);
        Assert.Contains("aria-label=\"Select Ada\"", small);
        Assert.DoesNotContain("Speaker results pages", small);
        Assert.DoesNotContain("<script>", small);
    }

    private static async Task<string> RenderAsync(List<Speaker> speakers, Guid? selectedId, int page = 1)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PagedSpeakerPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(SpeakerPicker.Speakers)] = speakers,
                [nameof(SpeakerPicker.SelectedSpeakerId)] = selectedId,
                [nameof(PagedSpeakerPicker.InitialPage)] = page
            }));
            return output.ToHtmlString();
        });
    }

    private static void SetProperty(SpeakerPicker picker, string name, object value) =>
        typeof(SpeakerPicker).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(picker, value);

    private static object? GetProperty(SpeakerPicker picker, string name) =>
        typeof(SpeakerPicker).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(picker);

    private static void SetField(SpeakerPicker picker, string name, object value) =>
        typeof(SpeakerPicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, value);

    private static object? GetField(SpeakerPicker picker, string name) =>
        typeof(SpeakerPicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(picker);

    public sealed class PagedSpeakerPicker : SpeakerPicker
    {
        [Parameter] public int InitialPage { get; set; }

        protected override void OnInitialized() => SetField(this, "page", InitialPage);
    }
}
