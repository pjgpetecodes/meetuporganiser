using MeetupOrganiser.Web;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;

namespace MeetupOrganiser.Tests;

public class MeetupMarkdownTests
{
    [Fact]
    public void Preview_FormatsSupportedMarkdownAndKeepsTemplatePlaceholders()
    {
        var html = MeetupMarkdown.ToHtml("# {{EventTitle}}\n\n**Speaker:** *Alex*\n\n- First\n- Second\n\n1. One\n2. Two\n\n[Site](https://example.com)");
        Assert.Contains("<p><strong>{{EventTitle}}</strong></p>", html);
        Assert.Contains("<strong>Speaker:</strong> <em>Alex</em>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>First</li>", html);
        Assert.Contains("href=\"https://example.com/\"", html);
    }

    [Fact]
    public void EditorHtml_PreservesHeadingsForMarkdownRoundTrip()
    {
        Assert.Contains("<h1>Event title</h1>", MeetupMarkdown.ToHtml("# Event title", preserveHeadings: true));
        Assert.Contains("<p><strong>Event title</strong></p>", MeetupMarkdown.ToHtml("# Event title"));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](data:text/html,evil)")]
    [InlineData("![image](https://external.example/tracker)")]
    public void Preview_DoesNotExecuteHtmlLoadImagesOrCreateUnsafeLinks(string input)
    {
        var html = MeetupMarkdown.ToHtml(input);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.DoesNotContain("href=\"data:", html);
    }

    [Fact]
    public async Task Editor_RendersRichTextAndKeyboardHelpWithoutMarkdownMarkers()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IJSRuntime>(new EditorJs()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MeetupMarkdownEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> {
                    [nameof(MeetupMarkdownEditor.Value)] = "**Title**\n\n{{SpeakerSection}}",
                    [nameof(MeetupMarkdownEditor.Label)] = "Group template"
                }));
            return output.ToHtmlString();
        });
        Assert.Contains("Group template formatting", html);
        Assert.Contains("contenteditable=\"true\"", html);
        Assert.Contains("<strong>Title</strong>", html);
        Assert.DoesNotContain("**Title**", html);
        Assert.Contains("Ctrl+B", html);
        Assert.Contains("{{SpeakerSection}}", html);
        Assert.Contains("Numbered list", html);
        Assert.Contains("bi-type-bold", html);
        Assert.Contains("bi-list-ol", html);
        Assert.Contains("aria-label=\"Undo\"", html);
        Assert.Contains("aria-label=\"Heading level\"", html);
        Assert.Contains("Normal text", html);
        Assert.Contains("value=\"h1\"", html);
        Assert.Contains("value=\"h6\"", html);
        Assert.DoesNotContain(">Bold</button>", html);
    }

    private sealed class EditorJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Editor_PropagatesRichTextMarkdownToBoundTemplate()
    {
        string? saved = null;
        var editor = new MeetupMarkdownEditor();
        typeof(MeetupMarkdownEditor).GetProperty(nameof(MeetupMarkdownEditor.ValueChanged))!
            .SetValue(editor, EventCallback.Factory.Create<string?>(new object(), value => saved = value));
        await editor.RichTextChanged("**{{EventTitle}}**");
        Assert.Equal("**{{EventTitle}}**", saved);
        Assert.Equal(saved, typeof(MeetupMarkdownEditor)
            .GetField("syncedValue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor));
    }
}
