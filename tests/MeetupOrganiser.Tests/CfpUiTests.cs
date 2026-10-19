using System.Reflection;
using System.Text.RegularExpressions;
using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Web;
using MeetupOrganiser.Web.Components;
using MeetupOrganiser.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class CfpUiTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicCfp_AfterEmailShowsOnlyConfirmationOnSuccessAndKeepsFormOnFailure(bool delivered)
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ISpeakerBookingService>(new BookingStub { VerificationDelivered = delivered })
            .AddSingleton<IBankHolidayService>(new BankHolidayStub())
            .AddSingleton<NavigationManager, TestNavigationManager>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<EmailRequestedCfp>();
            return output.ToHtmlString();
        });
        Assert.Equal(delivered, html.Contains("Check your email for a verification link.", StringComparison.Ordinal));
        Assert.Equal(!delivered, html.Contains("cfp-email", StringComparison.Ordinal));
        Assert.Equal(!delivered, html.Contains("Previous meetups", StringComparison.Ordinal));
        Assert.Equal(!delivered, html.Contains("Test group", StringComparison.Ordinal));
        if (!delivered) Assert.Contains("Delivery failed", html);
    }

    [Fact]
    public async Task PublicCfp_ShowsGroupInformationAndReadonlyPastMeetupCards()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ISpeakerBookingService>(new BookingStub())
            .AddSingleton<IBankHolidayService>(new BankHolidayStub())
            .AddSingleton<NavigationManager, TestNavigationManager>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PublicCfp>();
            return output.ToHtmlString();
        });
        Assert.Contains("Group description", html);
        Assert.Contains("Group location", html);
        Assert.Contains("src=\"https://example.com/logo.png\"", html);
        Assert.Contains("href=\"https://example.com/community\"", html);
        Assert.Contains("Previous meetups", html);
        var cards = html[html.IndexOf("<section", StringComparison.Ordinal)..];
        Assert.Contains("Previous public meetup", cards);
        Assert.Contains("href=\"https://www.meetup.com/group/events/123/\" target=\"_blank\" rel=\"noopener noreferrer\"", cards);
        Assert.DoesNotContain("<input", cards);
        Assert.DoesNotContain("<button", cards);
        Assert.DoesNotContain("/events/edit", cards);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    public async Task Submission_ShowsDateSummaryWhenAvailableAndImportOnlyWithoutExistingProfile(bool available, bool existingProfile, bool bankHoliday, bool validBranding)
    {
        var booking = new BookingStub { HasExistingProfile = existingProfile, ValidBranding = validBranding };
        var date = new DateTime(2027, 1, bankHoliday ? 1 : 25, 18, 30, 0, DateTimeKind.Utc);
        booking.Dates = [new(booking.DateId, date, 90, EventFormat.Hybrid, "CGI", "Nottingham", 0)];
        var holidays = new BankHolidayCalendar([new(new(2027, 1, 1), "New Year's Day")]);
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ISpeakerBookingService>(booking)
            .AddSingleton<IBankHolidayService>(new BankHolidayStub(holidays))
            .AddSingleton(DispatchProxy.Create<IBlobStorageService, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton(DispatchProxy.Create<ISessionizeProfileService, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton<SpeakerBookingNotifications>()
            .AddSingleton(DispatchProxy.Create<ISettingsService, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton(DispatchProxy.Create<IEmailService, CfpOrganiserRenderingTests.UnusedService>())
            .AddSingleton<NavigationManager, TestNavigationManager>().BuildServiceProvider();
        RenderedBookingPage.PreferredDate = available ? booking.DateId : Guid.NewGuid();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<RenderedBookingPage>();
            return output.ToHtmlString();
        });
        Assert.Equal(available, html.Contains("Your selected meetup", StringComparison.Ordinal));
        Assert.Equal(!available, html.Contains("Choose your meetup date", StringComparison.Ordinal));
        Assert.Equal(!existingProfile, html.Contains("Find on Sessionize", StringComparison.Ordinal));
        Assert.Equal(existingProfile, html.Contains("Create a new talk", StringComparison.Ordinal));
        Assert.Equal(existingProfile, html.Contains("Your previous meetup talks", StringComparison.Ordinal));
        Assert.Equal(existingProfile, html.Contains("Previous community talk", StringComparison.Ordinal));
        Assert.Contains("Talk Title", html);
        Assert.Contains("Confirm date and submit talk", html);
        Assert.Contains("Speaker and talk submission", html);
        Assert.Contains("Community branding description", html);
        Assert.Contains("<h1 class=\"h3 fw-bold mb-1\">Test group</h1>", html);
        Assert.Equal(validBranding, html.Contains("https://example.com/group-logo.png", StringComparison.Ordinal));
        Assert.Equal(validBranding, html.Contains("Visit Test group", StringComparison.Ordinal));
        Assert.Equal(validBranding, html.Contains("5px solid #123456", StringComparison.Ordinal));
        Assert.Equal(validBranding, html.Contains("3px solid #abcdef", StringComparison.Ordinal));
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("url(", html);
        if (available)
        {
            var summary = html[html.IndexOf("Your selected meetup", StringComparison.Ordinal)..];
            summary = summary[..summary.IndexOf("</div>", StringComparison.Ordinal)];
            Assert.Equal(bankHoliday, summary.Contains("public-holiday-badge", StringComparison.Ordinal));
            Assert.Equal(bankHoliday, summary.Contains("Public holiday:", StringComparison.Ordinal));
            Assert.DoesNotContain("Holiday dates remain selectable", summary);
        }
    }

    [Fact]
    public void PreviousMeetupTalkSelection_PrefillsEditableFieldsAndAllowsNewTalk()
    {
        var page = new SpeakerChooseDate();
        SetField(page, "invitation", new SpeakerBookingInvitation(Guid.NewGuid(), "Group", new Speaker(), false, true, true)
        {
            PreviousTalks = [new("Previous talk", "Previous abstract", "Other group", DateTime.UtcNow.AddMonths(-1))]
        });
        var choose = typeof(SpeakerChooseDate).GetMethod("ChoosePreviousTalk", BindingFlags.Instance | BindingFlags.NonPublic)!;
        choose.Invoke(page, [new ChangeEventArgs { Value = "0" }]);
        Assert.Equal("Previous talk", Field<string>(page, "talkTitle"));
        Assert.Equal("Previous abstract", Field<string>(page, "talkDescription"));
        choose.Invoke(page, [new ChangeEventArgs { Value = "new" }]);
        Assert.Empty(Field<string>(page, "talkTitle"));
        Assert.Empty(Field<string>(page, "talkDescription"));
    }

    [Fact]
    public void SessionizeImport_PreservesVerifiedEmailAndCopiesSelectedProfileDetails()
    {
        var page = new SpeakerChooseDate();
        var speaker = new Speaker { Email = "verified@example.com" };
        SetField(page, "speaker", speaker);
        var profile = new SessionizeProfile("ada", "https://sessionize.com/ada", "Ada", null, null,
            "I build things.", "https://sessionize.com/photo.png", [], [new("Existing talk", "Existing abstract", null)]);
        typeof(SpeakerChooseDate).GetMethod("ApplySessionizeProfile", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [(profile, SessionizeProfileFields.All)]);
        Assert.Equal("verified@example.com", speaker.Email);
        Assert.Equal("Ada", speaker.Name);
        Assert.Equal(profile.Bio, speaker.Bio);
        Assert.Equal(profile.PhotoUrl, speaker.PhotoBlobUrl);
        Assert.Same(profile, Field<SessionizeProfile>(page, "importedProfile"));
        typeof(SpeakerChooseDate).GetMethod("ChooseTalk", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [profile.Sessions[0]]);
        Assert.Equal("Existing talk", Field<string>(page, "talkTitle"));
        Assert.Equal("Existing abstract", Field<string>(page, "talkDescription"));
        typeof(SpeakerChooseDate).GetMethod("NewTalk", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        Assert.Empty(Field<string>(page, "talkTitle"));
        Assert.Empty(Field<string>(page, "talkDescription"));
    }

    [Fact]
    public async Task DateCards_MatchHolidayByUkDateAndKeepDateSelectable()
    {
        var id = Guid.NewGuid();
        var data = new BankHolidayCalendar([new(new(2027, 5, 3), "Early May bank holiday")]);
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub(data)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AvailableDatePicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(AvailableDatePicker.Dates)] = new List<AvailableMeetupDate>
                {
                    new(id, new DateTime(2027, 5, 2, 23, 30, 0, DateTimeKind.Utc), 90, EventFormat.Online, null, "Zoom", 1)
                },
                [nameof(AvailableDatePicker.SelectedDateId)] = id
            }));
            return output.ToHtmlString();
        });
        Assert.Contains("public-holiday-card", html);
        Assert.Contains("Public holiday: Early May bank holiday", html);
        Assert.Contains("public-holiday-badge", html);
        Assert.Contains("Monday 3 May 2027", html);
        Assert.Contains("Shared event: one speaker is already assigned.", html);
        var radio = Regex.Match(html, "<input[^>]*type=\"radio\"[^>]*>").Value;
        Assert.NotEmpty(radio);
        Assert.Contains("checked", radio);
        Assert.DoesNotContain("disabled", radio);
        var radioId = Regex.Match(radio, "id=\"([^\"]+)\"").Groups[1].Value;
        Assert.Contains($"for=\"{radioId}\"", html);
        Assert.Contains("border-primary border-2", html);
    }

    [Fact]
    public async Task DateCards_ShowUkTimeAndSharedWarningWithoutPrivateDetails()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IBankHolidayService>(new BankHolidayStub()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var dates = new List<AvailableMeetupDate>
        {
            new(Guid.NewGuid(), new DateTime(2027, 7, 19, 17, 30, 0, DateTimeKind.Utc), 90, EventFormat.Hybrid, "CGI", "Nottingham", 1),
            new(Guid.NewGuid(), new DateTime(2027, 8, 16, 17, 30, 0, DateTimeKind.Utc), 90, EventFormat.Online, null, "Zoom", 0)
        };
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AvailableDatePicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(AvailableDatePicker.Dates)] = dates
            }));
            return output.ToHtmlString();
        });

        Assert.Contains("18:30 BST", html);
        Assert.Contains("Shared event: one speaker is already assigned.", html);
        Assert.Contains("Two speaker places available.", html);
        Assert.Contains("Nottingham", html);
        Assert.DoesNotContain("TalkTitle", html);
    }

    [Fact]
    public async Task PublicEmailLink_AutomaticallyVerifiesAndOpensFormWithPreferredDate()
    {
        var service = new BookingStub { RequiresVerification = true };
        var page = CreatePage(service);
        SetProperty(page, "PreferredDateId", service.DateId);

        await InvokeAsync(page, "OnParametersSetAsync");

        Assert.Equal(1, service.VerificationCalls);
        Assert.Equal(1, service.AvailabilityCalls);
        Assert.False(Field<SpeakerBookingInvitation>(page, "invitation").RequiresEmailVerification);
        Assert.Equal("ada@example.com", Field<Speaker>(page, "speaker").Email);
        Assert.Equal(service.DateId, Field<Guid?>(page, "selectedDateId"));
    }

    [Fact]
    public async Task EmailVerificationFailure_DoesNotShowProfileOrBookAndCanBeRetried()
    {
        var service = new BookingStub { RequiresVerification = true, VerificationFails = true };
        var page = CreatePage(service);
        await InvokeAsync(page, "OnParametersSetAsync");
        Assert.True(Field<SpeakerBookingInvitation>(page, "invitation").RequiresEmailVerification);
        Assert.Equal(0, service.AvailabilityCalls);
        Assert.Null(Field<Speaker>(page, "speaker").Email);
        Assert.Contains("expired", Field<string>(page, "error"));
        Assert.Null(Field<MeetupEvent?>(page, "bookedEvent"));
        service.VerificationFails = false;
        await InvokeAsync(page, "VerifyEmailAsync");
        Assert.False(Field<SpeakerBookingInvitation>(page, "invitation").RequiresEmailVerification);
        Assert.Equal("ada@example.com", Field<Speaker>(page, "speaker").Email);
    }

    [Fact]
    public async Task PublicCfp_RendersEnabledWholeCardDateChoices()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ISpeakerBookingService>(new BookingStub())
            .AddSingleton<IBankHolidayService>(new BankHolidayStub())
            .AddSingleton<NavigationManager, TestNavigationManager>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PublicCfp>();
            return output.ToHtmlString();
        });
        var radio = Regex.Match(html, "<input[^>]*type=\"radio\"[^>]*>").Value;
        Assert.NotEmpty(radio);
        Assert.DoesNotContain("disabled", radio);
        Assert.DoesNotMatch("<fieldset[^>]*disabled", html);
        var id = Regex.Match(radio, "id=\"([^\"]+)\"").Groups[1].Value;
        Assert.Contains($"for=\"{id}\"", html);
    }

    [Fact]
    public async Task PublicCfp_DatePreferenceIsPassedToVerificationWithoutBooking()
    {
        var service = new BookingStub();
        var page = new PublicCfp();
        SetProperty(page, "BookingService", service);
        SetProperty(page, "Navigation", new TestNavigationManager());
        SetProperty(page, "Logger", NullLogger<PublicCfp>.Instance);
        SetField(page, "selectedDateId", service.DateId);
        await InvokeAsync(page, "RequestVerificationAsync");
        Assert.Equal(service.DateId, service.VerificationDateId);
    }

    [Fact]
    public async Task CapacityConflict_RefreshesDatesAndPreservesProposalAndProfile()
    {
        var service = new BookingStub();
        var page = CreatePage(service);
        await InvokeAsync(page, "OnParametersSetAsync");
        SetField(page, "talkTitle", "My cloud talk");
        SetField(page, "talkDescription", "My abstract");
        SetField(page, "selectedDateId", service.DateId);
        Field<Speaker>(page, "speaker").Bio = "My edited bio";
        service.Dates = [];

        await InvokeAsync(page, "BookAsync");

        Assert.Equal("My cloud talk", Field<string>(page, "talkTitle"));
        Assert.Equal("My abstract", Field<string>(page, "talkDescription"));
        Assert.Equal("My edited bio", Field<Speaker>(page, "speaker").Bio);
        Assert.Null(Field<Guid?>(page, "selectedDateId"));
        Assert.Contains("filled", Field<string>(page, "error"));
        Assert.Null(Field<MeetupEvent?>(page, "bookedEvent"));
    }

    private static SpeakerChooseDate CreatePage(BookingStub service)
    {
        var page = new SpeakerChooseDate();
        SetProperty(page, "Token", "private-token");
        SetProperty(page, "BookingService", service);
        SetProperty(page, "Logger", NullLogger<SpeakerChooseDate>.Instance);
        return page;
    }

    private static async Task InvokeAsync(object component, string name) =>
        await (Task)component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;

    private static T Field<T>(object component, string name) =>
        (T)component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private static void SetField(object component, string name, object? value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static void SetProperty(object component, string name, object value) =>
        component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class BookingStub : ISpeakerBookingService
    {
        public Guid DateId { get; } = Guid.NewGuid();
        public bool RequiresVerification { get; set; }
        public int VerificationCalls { get; private set; }
        public int AvailabilityCalls { get; private set; }
        public Guid? VerificationDateId { get; private set; }
        public bool VerificationFails { get; set; }
        public bool HasExistingProfile { get; set; }
        public bool ValidBranding { get; set; } = true;
        public bool VerificationDelivered { get; set; } = true;
        public IReadOnlyList<AvailableMeetupDate>? Dates { get; set; }

        public Task<IReadOnlyList<AvailableMeetupDate>> GetAvailableDatesAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            AvailabilityCalls++;
            return Task.FromResult(Dates ?? [new AvailableMeetupDate(DateId, DateTime.UtcNow.AddDays(30), 90, EventFormat.Online, null, "Zoom", 1)]);
        }

        public Task<SpeakerBookingInvitation?> GetInvitationAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult<SpeakerBookingInvitation?>(new(Guid.NewGuid(), "Test group",
                new Speaker { Name = "Ada", Email = RequiresVerification ? null : "ada@example.com" }, RequiresVerification, true, HasExistingProfile)
            {
                GroupLogoUrl = ValidBranding ? "https://example.com/group-logo.png" : "javascript:alert(1)",
                GroupDescription = "Community branding description",
                GroupWebsiteUrl = ValidBranding ? "https://example.com/community" : "javascript:alert(1)",
                GroupPrimaryColorHex = ValidBranding ? "#123456" : "red; background: url(https://example.com)",
                GroupSecondaryColorHex = ValidBranding ? "#abcdef" : null,
                PreviousTalks = HasExistingProfile && !RequiresVerification
                    ? [new("Previous community talk", "Earlier abstract", "Other group", DateTime.UtcNow.AddMonths(-1))]
                    : []
            });

        public Task<BookingActionResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
        {
            VerificationCalls++;
            if (VerificationFails) return Task.FromResult(new BookingActionResult(false, "This link has expired."));
            RequiresVerification = false;
            return Task.FromResult(new BookingActionResult(true, "Verified"));
        }

        public Task<SpeakerBookingResult> BookAsync(string token, Guid eventId, Speaker profile, string talkTitle, string? talkDescription, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SpeakerBookingResult(false, "That date has filled. Please choose another."));

        public Task<PublicCfpGroup?> GetPublicGroupAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<PublicCfpGroup?>(new(Guid.NewGuid(), "Test group", "Group description")
            {
                LogoUrl = "https://example.com/logo.png", WebsiteUrl = "https://example.com/community",
                DefaultLocation = "Group location",
                PreviousEvents = [new("Previous public meetup", DateTime.UtcNow.AddMonths(-1), 90,
                    EventFormat.Online, null, "Zoom", "https://www.meetup.com/group/events/123/")]
            });
        public Task<string> CreateInvitationAsync(Guid groupId, Guid speakerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BookingActionResult> RequestVerificationAsync(string key, string email, string baseUrl, Guid? preferredDateId = null, CancellationToken cancellationToken = default)
        {
            VerificationDateId = preferredDateId;
            return Task.FromResult(new BookingActionResult(VerificationDelivered, VerificationDelivered ? "Sent" : "Delivery failed"));
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://example.com/", "https://example.com/cfp/test");
    }

    public sealed class RenderedBookingPage : SpeakerChooseDate
    {
        public static Guid PreferredDate { get; set; }
        public RenderedBookingPage() => PreferredDateId = PreferredDate;
    }

    public sealed class EmailRequestedCfp : PublicCfp
    {
        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();
            await (Task)typeof(PublicCfp).GetMethod("RequestVerificationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null)!;
        }
    }
}
