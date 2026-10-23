using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeetupOrganiser.Tests;

public class HostedBrowserTests
{
    [Theory]
    [InlineData("invalid_payload", "expired")]
    [InlineData("browser_launch", "Chrome")]
    [InlineData("navigation", "network")]
    [InlineData("arbitrary-secret-value", "Node")]
    public void StartupErrors_ExplainFailureWithoutEchoingWorkerData(string code, string expected)
    {
        var message = HostedBrowserSessions.StartupMessage(code);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("arbitrary-secret-value", message);
    }

    [Fact]
    public async Task LoginState_ExpiresAfterSevenDaysAndIsRemovedOnLoad()
    {
        var folder = Path.Combine(Path.GetTempPath(), "meetup-browser-expiry-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["HostedBrowser:LoginDirectory"] = folder }).Build();
        var clock = new Clock();
        var store = new HostedBrowserLoginStore(new EphemeralDataProtectionProvider(), configuration, clock);
        using var document = JsonDocument.Parse("""{"cookies":[],"origins":[]}""");
        try
        {
            await store.SaveAsync("owner", document.RootElement);
            clock.UtcNow = clock.UtcNow.AddDays(7);
            Assert.Null(await store.LoadAsync("owner"));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
    }

    [Fact]
    public async Task LoginState_IsProtectedScopedToOwnerAndDeletedExplicitly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "meetup-browser-login-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["HostedBrowser:LoginDirectory"] = folder }).Build();
        var store = new HostedBrowserLoginStore(new EphemeralDataProtectionProvider(), configuration);
        using var document = JsonDocument.Parse("""{"cookies":[{"name":"test","value":"secret-cookie"}],"origins":[]}""");
        try
        {
            Assert.Null(await store.LoadAsync("owner"));
            await store.SaveAsync("owner", document.RootElement);
            var file = Directory.GetFiles(folder).Single();
            Assert.DoesNotContain("secret-cookie", await File.ReadAllTextAsync(file));
            Assert.Null(await store.LoadAsync("other"));
            Assert.Equal("secret-cookie", (await store.LoadAsync("owner"))!.Value.GetProperty("cookies")[0].GetProperty("value").GetString());
            await File.WriteAllTextAsync(file, "tampered");
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync("owner"));
            store.Forget("owner");
            Assert.Null(await store.LoadAsync("owner"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
    }

    [Fact]
    public void Owner_IsTenantScopedAndRequiresStableIdentity()
    {
        ClaimsPrincipal User(string tenant) => new(new ClaimsIdentity([
            new Claim("tid", tenant), new Claim(ClaimTypes.NameIdentifier, "same-user")], "test"));
        Assert.NotEqual(HostedBrowserSessions.Owner(User("one")), HostedBrowserSessions.Owner(User("two")));
        Assert.Throws<InvalidOperationException>(() => HostedBrowserSessions.Owner(new ClaimsPrincipal()));
    }

    [LocalBrowserFact]
    public async Task AuthenticatedApp_CanLaunchFillAndProxyBrowserWithoutExposingWorkerCredentials()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MeetupOrganiser.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var loginFolder = Path.Combine(Path.GetTempPath(), "meetup-hosted-test-" + Guid.NewGuid().ToString("N"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HostedBrowser:Enabled"] = "true",
            ["HostedBrowser:AllowLiveMeetup"] = "false",
            ["HostedBrowser:WorkerScript"] = Path.Combine(root!.FullName, "tools", "hosted-browser-prototype", "server.js"),
            ["HostedBrowser:LoginDirectory"] = loginFolder
        });
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.Events.OnValidatePrincipal = LocalOrganizerCookie.ValidatePrincipalAsync);
        builder.Services.AddAuthorization(options => options.AddPolicy("OrganizerOnly", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton<HostedBrowserLoginStore>();
        builder.Services.AddSingleton<HostedBrowserSessions>();
        builder.Services.AddHostedService(services => services.GetRequiredService<HostedBrowserSessions>());
        await using var app = builder.Build();
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHostedBrowser();
        app.MapGet("/test-legacy-login", async (HttpContext context) =>
        {
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Legacy organiser")], "test")));
            return "ok";
        });
        app.MapGet("/test-login/{owner}", async (HttpContext context, string owner) =>
        {
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner)], "test")));
            return "ok";
        });
        app.MapGet("/test-token", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken);
        await app.StartAsync();
        var cookies = new CookieContainer();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies })
        { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(90) };
        try
        {
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync($"/hosted-browser/{Guid.NewGuid()}/")).StatusCode);
            await client.GetAsync("/test-legacy-login");
            var legacy = await client.GetAsync($"/hosted-browser/{Guid.NewGuid()}/");
            Assert.Equal(HttpStatusCode.Redirect, legacy.StatusCode);
            Assert.Contains("Login", legacy.Headers.Location!.ToString());
            await client.GetAsync("/test-login/owner");
            var payload = MeetupWebsite.Prepare(new MeetupEvent
            {
                MeetupGroup = new() { MeetupUrlName = "local-review" },
                EventDateTimeUtc = new(2027, 1, 25, 18, 30, 0, DateTimeKind.Utc)
            }, "App prepared event", "App prepared description",
                Convert.ToBase64String([137, 80, 78, 71, 13, 10, 26, 10, 0]));
            var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/hosted-browser/start",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["payload"] = json }))).StatusCode);
            var token = await client.GetStringAsync("/test-token");
            var invalidPayload = await client.PostAsync("/hosted-browser/start",
                new FormUrlEncodedContent(new Dictionary<string, string>
                { ["payload"] = "{sensitive-event-data", ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, invalidPayload.StatusCode);
            var diagnostic = await invalidPayload.Content.ReadAsStringAsync();
            Assert.Contains("reading prepared event content", diagnostic);
            Assert.Contains("JsonException", diagnostic);
            Assert.Contains("requestId", diagnostic);
            Assert.DoesNotContain("sensitive-event-data", diagnostic);
            using var remembered = JsonDocument.Parse("""{"cookies":[{"name":"synthetic-login","value":"test-only-cookie","domain":".meetup.com","path":"/","expires":-1,"httpOnly":true,"secure":true,"sameSite":"Lax"}],"origins":[]}""");
            await app.Services.GetRequiredService<HostedBrowserLoginStore>().SaveAsync("local:owner", remembered.RootElement);
            var started = await client.PostAsync("/hosted-browser/start", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["payload"] = json, ["__RequestVerificationToken"] = token, ["remember"] = "true" }));
            Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
            var path = started.Headers.Location!.ToString();
            var duplicate = await client.PostAsync("/hosted-browser/start", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["payload"] = json, ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, duplicate.StatusCode);
            Assert.Contains("End your current browser session", await duplicate.Content.ReadAsStringAsync());
            Assert.StartsWith("/hosted-browser/", path);
            var viewer = await client.GetStringAsync(path);
            Assert.DoesNotContain("offline_session=", viewer);
            Assert.Contains("viewer.js", viewer);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + "state")).StatusCode);
            using var socket = new ClientWebSocket();
            socket.Options.Cookies = cookies;
            socket.Options.SetRequestHeader("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            await socket.ConnectAsync(new Uri(client.BaseAddress.AbsoluteUri.Replace("http:", "ws:") + path.TrimStart('/') + "stream"), default);
            await socket.SendAsync(Encoding.UTF8.GetBytes("""{"type":"fill"}"""), WebSocketMessageType.Text, true, default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[1024 * 1024];
            var found = false;
            while (!found)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                if (received.MessageType != WebSocketMessageType.Text) continue;
                using var result = JsonDocument.Parse(message.ToArray());
                if (result.RootElement.GetProperty("type").GetString() != "report") continue;
                Assert.Empty(result.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray());
                Assert.Contains(result.RootElement.GetProperty("report").GetProperty("filled").EnumerateArray(),
                    field => field.GetString() == "Title");
                found = true;
            }
            using var remember = new HttpRequestMessage(HttpMethod.Post, path + "remember");
            remember.Headers.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(remember)).StatusCode);
            Assert.Single(Directory.GetFiles(loginFolder));
            var restored = await app.Services.GetRequiredService<HostedBrowserLoginStore>().LoadAsync("local:owner");
            Assert.Contains(restored!.Value.GetProperty("cookies").EnumerateArray(),
                cookie => cookie.GetProperty("value").GetString() == "test-only-cookie");
            await client.GetAsync("/test-login/other");
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            await client.GetAsync("/test-login/owner");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path + "end", null)).StatusCode);
            using var forget = new HttpRequestMessage(HttpMethod.Post, path + "forget");
            forget.Headers.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(forget)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            Assert.Empty(Directory.GetFiles(loginFolder));
            builder.Configuration["HostedBrowser:NodeExecutable"] = Path.Combine(loginFolder, "missing-node.exe");
            var missingNode = await Assert.ThrowsAsync<HostedBrowserStartupException>(() =>
                app.Services.GetRequiredService<HostedBrowserSessions>().StartAsync("local:diagnostic", payload));
            Assert.Contains("Node could not start", missingNode.Message);
        }
        finally
        {
            await app.StopAsync();
            if (Directory.Exists(loginFolder)) Directory.Delete(loginFolder);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}

public sealed class LocalBrowserFactAttribute : FactAttribute
{
    public LocalBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MEETUP_HOSTED_BROWSER_TESTS") != "1")
            Skip = "Set MEETUP_HOSTED_BROWSER_TESTS=1 after installing Node dependencies and Chrome to run the real local-worker test.";
    }
}
