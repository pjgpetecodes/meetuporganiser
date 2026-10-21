using System.Reflection;
using System.Text.RegularExpressions;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Tests;

public class MeetupDateTimePickerTests
{
    [Fact]
    public async Task WhileHolidayDataLoads_CorrectMonthRemainsUsableAndLoadingIsExplicit()
    {
        var service = new LoadingHolidayService();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(service).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() =>
            renderer.BeginRenderingComponent<MeetupDateTimePicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(MeetupDateTimePicker.Value)] = new DateTime(2027, 12, 27, 18, 30, 0) })));
        var loadingHtml = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Loading public holiday information", loadingHtml);
        Assert.Contains("December 2027", loadingHtml);
        var button = Regex.Match(loadingHtml, "<button[^>]*aria-label=\"Monday 27 December 2027\"[^>]*>").Value;
        Assert.NotEmpty(button);
        Assert.DoesNotContain("disabled", button);

        service.Completion.SetResult(new BankHolidayCalendar([new(new(2027, 12, 27), "Christmas Day", "Substitute day")]));
        await root.QuiescenceTask;
        var loadedHtml = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.DoesNotContain("Loading public holiday information", loadedHtml);
        Assert.Contains("Public holiday: Christmas Day (Substitute day)", loadedHtml);
    }

    [Fact]
    public async Task Holiday_ShowsMarkerNameAndSelectedStateWithoutDisablingDate()
    {
        var data = new BankHolidayCalendar([new(new(2027, 12, 27), "Christmas Day", "Substitute day")]);
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub(data)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MeetupDateTimePicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(MeetupDateTimePicker.Value)] = new DateTime(2027, 12, 27, 18, 30, 0) }));
            return output.ToHtmlString();
        });
        var holidayButton = Regex.Match(html, "<button[^>]*aria-label=\"Monday 27 December 2027\"[^>]*>.*?</button>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(holidayButton);
        Assert.Contains("calendar-day public-holiday btn-primary", holidayButton);
        Assert.Contains("aria-pressed=\"true\"", holidayButton);
        Assert.Contains("public-holiday-badge", holidayButton);
        Assert.Contains("Christmas Day (Substitute day)", holidayButton);
        Assert.Contains("aria-describedby=", holidayButton);
        Assert.DoesNotContain("disabled", holidayButton);
        Assert.Contains("Public holiday: Christmas Day (Substitute day)", html[..html.IndexOf("</summary>", StringComparison.Ordinal)]);
        Assert.Contains("England and Wales", html);

        DateTime? selected = null;
        var picker = new MeetupDateTimePicker
        {
            Value = new(2027, 12, 1, 18, 30, 0),
            BankHolidayService = new BankHolidayStub(data),
            ValueChanged = EventCallback.Factory.Create<DateTime>(new object(), value => selected = value)
        };
        await InvokeAsync(picker, "OnInitializedAsync");
        await InvokeAsync(picker, "SelectDateAsync", new DateOnly(2027, 12, 27));
        Assert.Equal(new DateTime(2027, 12, 27, 18, 30, 0), selected);
    }

    [Theory]
    [InlineData(false, "Public holiday information is unavailable")]
    [InlineData(true, "Public holiday information is not published for 2032")]
    public async Task MissingHolidayData_ShowsWarningButLeavesCalendarUsable(bool hasData, string warning)
    {
        var data = hasData ? new BankHolidayCalendar([new(new(2027, 1, 1), "New Year's Day")]) : BankHolidayCalendar.Unavailable;
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub(data)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MeetupDateTimePicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(MeetupDateTimePicker.Value)] = new DateTime(2032, 2, 10, 18, 30, 0) }));
            return output.ToHtmlString();
        });
        Assert.Contains(warning, html);
        var button = Regex.Match(html, "<button[^>]*aria-label=\"Tuesday 10 February 2032\"[^>]*>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(button);
        Assert.DoesNotContain("disabled", button);
    }

    [Theory]
    [InlineData(26, false)]
    [InlineData(27, true)]
    public async Task SelectedDate_ShowsHolidayKeyOnlyForAnActualHoliday(int day, bool isHoliday)
    {
        var data = new BankHolidayCalendar([new(new(2027, 12, 27), "Christmas Day", "Substitute day")]);
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub(data)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MeetupDateTimePicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(MeetupDateTimePicker.Value)] = new DateTime(2027, 12, day, 18, 30, 0) }));
            return output.ToHtmlString();
        });
        var summary = html[..html.IndexOf("</summary>", StringComparison.Ordinal)];
        var belowCalendar = html[(html.IndexOf("</details>", StringComparison.Ordinal) + "</details>".Length)..];
        Assert.Equal(isHoliday, summary.Contains("public-holiday-badge", StringComparison.Ordinal));
        Assert.Equal(isHoliday, belowCalendar.Contains("Public holiday (England and Wales)", StringComparison.Ordinal));
        Assert.Contains("Public holiday: Christmas Day", html);
    }

    [Fact]
    public async Task Calendar_ShowsWeekdaysAndCorrectLeapMonthAlignment()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MeetupDateTimePicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(MeetupDateTimePicker.Value)] = new DateTime(2032, 2, 10, 18, 30, 0) }));
            return output.ToHtmlString();
        });

        Assert.Contains("Tuesday 10 February 2032", html);
        Assert.Equal(new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
            Regex.Matches(html, @"<th\b[^>]*>(.*?)</th>").Select(match => match.Groups[1].Value));
        Assert.Equal(29, Regex.Matches(html, "class=\"btn btn-sm calendar-day").Count);
        var firstRow = Regex.Match(html, @"<tbody[^>]*>\s*<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(6, Regex.Matches(firstRow, @"<td[^>]*>\s*</td>").Count);
        Assert.Contains("Sunday 1 February 2032", firstRow);
        Assert.Contains("value=\"18:30\"", html);
        Assert.DoesNotContain("datetime-local", html);
    }

    [Fact]
    public async Task DateAndTimeSelection_PreserveOtherValueAndNavigateAcrossYears()
    {
        var original = new DateTime(2032, 12, 15, 18, 30, 0);
        DateTime? selected = null;
        var picker = new MeetupDateTimePicker
        {
            Value = original,
            ValueChanged = EventCallback.Factory.Create<DateTime>(new object(), value => selected = value)
        };
        Invoke(picker, "OnParametersSet");
        Invoke(picker, "MoveMonth", 1);
        Assert.Equal(new DateOnly(2033, 1, 1), Field<DateOnly>(picker, "month"));
        await InvokeAsync(picker, "SelectDateAsync", new DateOnly(2033, 1, 20));
        Assert.Equal(new DateTime(2033, 1, 20, 18, 30, 0), selected);
        await InvokeAsync(picker, "ChangeTimeAsync", new ChangeEventArgs { Value = "20:15" });
        Assert.Equal(new DateTime(2032, 12, 15, 20, 15, 0), selected);

        Invoke(picker, "ChangeYear", new ChangeEventArgs { Value = "2036" });
        Invoke(picker, "ChangeMonth", new ChangeEventArgs { Value = "2" });
        Invoke(picker, "OnParametersSet");
        Assert.Equal(new DateOnly(2036, 2, 1), Field<DateOnly>(picker, "month"));

        selected = null;
        picker.Disabled = true;
        await InvokeAsync(picker, "SelectDateAsync", new DateOnly(2036, 2, 1));
        await InvokeAsync(picker, "ChangeTimeAsync", new ChangeEventArgs { Value = "21:00" });
        Assert.Null(selected);
    }

    private static object? Invoke(object picker, string method, params object[] args) =>
        typeof(MeetupDateTimePicker).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, args);
    private static async Task InvokeAsync(object picker, string method, params object[] args) =>
        await (Task)Invoke(picker, method, args)!;
    private static T Field<T>(object picker, string name) =>
        (T)typeof(MeetupDateTimePicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(picker)!;

    private sealed class LoadingHolidayService : IBankHolidayService
    {
        public TaskCompletionSource<BankHolidayCalendar> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BankHolidayCalendar> GetCalendarAsync(CancellationToken cancellationToken = default) => Completion.Task;
    }
}
