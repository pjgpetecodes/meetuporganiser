using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MeetupOrganiser.Core.DTOs;
using Microsoft.AspNetCore.Antiforgery;

namespace MeetupOrganiser.Web;

public sealed class HostedBrowserSessions(IConfiguration configuration, IHostEnvironment environment,
    ILogger<HostedBrowserSessions> logger, HostedBrowserLoginStore logins) : BackgroundService
{
    private readonly Dictionary<Guid, Session> sessions = [];
    private readonly SemaphoreSlim gate = new(1);
    public bool Enabled => environment.IsDevelopment() && configuration.GetValue<bool>("HostedBrowser:Enabled");

    public static string Owner(ClaimsPrincipal user) =>
        $"{user.FindFirstValue("tid") ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid") ?? "local"}:" +
        (user.FindFirstValue("oid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("A stable organiser identity is required."));

    public async Task<Guid> StartAsync(string owner, MeetupWebsiteHandoff payload, bool remember = false,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) throw new HostedBrowserStartupException("Local browser review is disabled. Enable HostedBrowser__Enabled in the Development launch configuration and restart F5.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var expiredId in sessions.Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow ||
                         pair.Value.Process.HasExited).Select(pair => pair.Key).ToArray())
            {
                sessions[expiredId].Dispose();
                sessions.Remove(expiredId);
            }
            if (sessions.Values.Any(session => session.Owner == owner))
                throw new HostedBrowserStartupException("End your current browser session before starting another.");
            if (sessions.Count >= 3) throw new HostedBrowserStartupException("All three local browser places are occupied. End an unused session first.");
            var script = Path.GetFullPath(configuration["HostedBrowser:WorkerScript"] ??
                Path.Combine(environment.ContentRootPath, "..", "..", "tools", "hosted-browser-prototype", "server.js"));
            if (!File.Exists(script)) throw new HostedBrowserStartupException("The local browser worker script is missing. Check HostedBrowser:WorkerScript.");
            var start = new ProcessStartInfo(configuration["HostedBrowser:NodeExecutable"] ?? "node")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("--managed");
            Process process;
            try
            {
                process = Process.Start(start) ?? throw new HostedBrowserStartupException("Could not start Node for the local browser worker.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new HostedBrowserStartupException("Node could not start. Check Node is installed and available to VS Code, or configure HostedBrowser:NodeExecutable with its full path.");
            }
            // Drain without retaining browser diagnostics, which can contain session URLs.
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            HttpClient? http = null;
            var stage = "loading remembered login";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var storageState = remember ? await logins.LoadAsync(owner, timeout.Token) : null;
                stage = "sending prepared content to Node";
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    payload,
                    headless = !configuration.GetValue<bool>("HostedBrowser:ShowBrowserWindow"),
                    live = configuration.GetValue<bool>("HostedBrowser:AllowLiveMeetup"),
                    storageState,
                    lifetimeMs = 30 * 60_000,
                    idleMs = 10 * 60_000
                }, JsonSerializerOptions.Web).AsMemory(), timeout.Token);
                process.StandardInput.Close();
                stage = "waiting for Chrome and the Meetup page";
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                using var document = JsonDocument.Parse(line ?? throw new HostedBrowserStartupException(
                    "Node exited before the browser was ready. Check the worker dependencies with npm ci --prefix .\\tools\\hosted-browser-prototype and restart F5."));
                if (document.RootElement.TryGetProperty("error", out var workerError))
                    throw new HostedBrowserStartupException(StartupMessage(workerError.GetString()));
                var launch = new Uri(document.RootElement.GetProperty("url").GetString()!);
                if (launch.Scheme != "http" || launch.Host != "127.0.0.1" || launch.Port <= 0)
                    throw new InvalidOperationException("The browser worker returned an invalid local endpoint.");
                var origin = launch.GetLeftPart(UriPartial.Authority);
                stage = "connecting to the local worker";
                http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(), UseProxy = false })
                { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(40) };
                using var authorize = new HttpRequestMessage(HttpMethod.Post, "/authorize")
                { Content = JsonContent.Create(new { token = launch.Fragment.TrimStart('#') }) };
                authorize.Headers.Add("Origin", origin);
                using var result = await http.SendAsync(authorize, timeout.Token);
                result.EnsureSuccessStatusCode();
                var cookie = result.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
                var id = Guid.NewGuid();
                sessions.Add(id, new Session(owner, process, http, cookie, DateTimeOffset.UtcNow.AddMinutes(30), remember));
                return id;
            }

            catch (Exception ex)
            {
                http?.Dispose();
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                finally { process.Dispose(); }
                if (ex is HostedBrowserStartupException) throw;
                if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    throw new HostedBrowserStartupException($"Browser startup timed out while {stage}. Prepare fresh content and retry.");
                if (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or UnauthorizedAccessException)
                    throw new HostedBrowserStartupException($"Browser startup failed while {stage} ({ex.GetType().Name}). " +
                        (stage == "loading remembered login" ? "Try launching with remembered login unchecked." :
                        "Check Node, Chrome and the local network connection, then retry."));
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public static string StartupMessage(string? code) => code switch
    {
        "invalid_payload" => "Prepared event content is invalid or expired. Return to Socials & Slides and prepare it again.",
        "browser_launch" => "Chrome could not launch. Check that the configured Chrome/Edge browser is installed and can start on this computer.",
        "navigation" => "The browser launched, but could not load the Meetup event page within 30 seconds. Check network access to Meetup and prepare the event again.",
        _ => "The browser worker could not initialise. Check Node and run npm ci --prefix .\\tools\\hosted-browser-prototype, then restart F5."
    };

    public async Task<Session?> FindAsync(Guid id, string owner, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return sessions.TryGetValue(id, out var session) && session.Owner == owner &&
                session.ExpiresAt > DateTimeOffset.UtcNow && !session.Process.HasExited ? session : null;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> RememberAsync(Guid id, string owner, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!sessions.TryGetValue(id, out var session) || session.Owner != owner || !session.Remember ||
                session.ExpiresAt <= DateTimeOffset.UtcNow || session.Process.HasExited) return false;
            using var request = new HttpRequestMessage(HttpMethod.Post, "/state");
            request.Headers.Add("Origin", session.Http.BaseAddress!.GetLeftPart(UriPartial.Authority));
            using var response = await session.Http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var state = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            await logins.SaveAsync(owner, state, cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task EndAsync(Guid id, string owner, CancellationToken cancellationToken = default, bool forgetLogin = false)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (sessions.TryGetValue(id, out var session) && session.Owner == owner)
            {
                sessions.Remove(id);
                session.Dispose();
            }
            if (forgetLogin) logins.Forget(owner);
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await gate.WaitAsync(stoppingToken);
                try
                {
                    foreach (var id in sessions.Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow ||
                                 pair.Value.Process.HasExited).Select(pair => pair.Key).ToArray())
                    {
                        sessions[id].Dispose();
                        sessions.Remove(id);
                        logger.LogInformation("Closed an expired local browser session.");
                    }
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await gate.WaitAsync(CancellationToken.None);
            try { foreach (var session in sessions.Values) session.Dispose(); sessions.Clear(); }
            finally { gate.Release(); }
        }
    }

    public sealed class Session(string owner, Process process, HttpClient http, string cookie, DateTimeOffset expiresAt,
        bool remember) : IDisposable
    {
        public string Owner { get; } = owner;
        public Process Process { get; } = process;
        public HttpClient Http { get; } = http;
        public string Cookie { get; } = cookie;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public bool Remember { get; } = remember;
        public void Dispose()
        {
            Http.Dispose();
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (Process.HasExited) { }
            Process.Dispose();
        }
    }
}

public sealed class HostedBrowserStartupException(string message) : InvalidOperationException(message);

public static class HostedBrowserApi
{
    public static void MapHostedBrowser(this WebApplication app)
    {
        app.MapPost("/hosted-browser/start", async (HttpContext context, HostedBrowserSessions sessions, IAntiforgery antiforgery,
            ILogger<HostedBrowserSessions> logger) =>
        {
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IFormFeature>(
                new Microsoft.AspNetCore.Http.Features.FormFeature(context.Request,
                    new Microsoft.AspNetCore.Http.Features.FormOptions { ValueLengthLimit = 14_200_000 }));
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid browser launch request. Reload the event page and try again."); }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var json = form["payload"].ToString();
            if (json.Length is 0 or > 14_200_000) return Results.BadRequest("Prepare current event content first.");
            var stage = "reading prepared event content";
            try
            {
                var payload = JsonSerializer.Deserialize<MeetupWebsiteHandoff>(json, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("Prepared content is missing.");
                stage = "identifying the signed-in organiser";
                var owner = HostedBrowserSessions.Owner(context.User);
                stage = "starting the browser session";
                var id = await sessions.StartAsync(owner, payload,
                    form["remember"] == "true", context.RequestAborted);
                return Results.Redirect($"/hosted-browser/{id}/");
            }
            catch (HostedBrowserStartupException ex)
            {
                logger.LogWarning("Local browser startup rejected: {Reason}", ex.Message);
                return Results.Problem(ex.Message, statusCode: 503);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or OperationCanceledException
                or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Local browser startup failed while {Stage} ({ErrorType}); request {RequestId}.",
                    stage, ex.GetType().Name, context.TraceIdentifier);
                return Results.Problem($"Browser startup failed while {stage} ({ex.GetType().Name}). " +
                    "Copy this response when reporting the error. No event content or login credentials are included.",
                    statusCode: 503, extensions: new Dictionary<string, object?>
                    { ["requestId"] = context.TraceIdentifier, ["stage"] = stage, ["errorType"] = ex.GetType().Name });
            }

        }).RequireAuthorization("OrganizerOnly").DisableAntiforgery();

        app.MapMethods("/hosted-browser/{id:guid}/{**path}", ["GET", "POST"], async (HttpContext context, Guid id,
            string? path, HostedBrowserSessions sessions, ILogger<HostedBrowserSessions> logger) =>
        {
            if (!sessions.Enabled) { context.Response.StatusCode = 404; return; }
            var owner = HostedBrowserSessions.Owner(context.User);
            var session = await sessions.FindAsync(id, owner, context.RequestAborted);
            if (session is null) { context.Response.StatusCode = 404; return; }
            context.Response.Headers.CacheControl = "no-store";
            path = string.IsNullOrEmpty(path) ? "/" : "/" + path;
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            if ((context.Request.Method == "POST" || context.WebSockets.IsWebSocketRequest) &&
                context.Request.Headers.Origin != origin)
            { context.Response.StatusCode = 403; return; }
            try
            {
                if (context.Request.Method == "POST")
                {
                    if (path == "/forget")
                    {
                        await sessions.EndAsync(id, owner, context.RequestAborted, forgetLogin: true);
                        context.Response.StatusCode = 204;
                        return;
                    }
                    if (path == "/remember" && session.Remember)
                    {
                        context.Response.StatusCode = await sessions.RememberAsync(id, owner, context.RequestAborted) ? 204 : 404;
                        return;
                    }
                    if (path == "/end")
                    {
                        await sessions.EndAsync(id, owner, context.RequestAborted);
                        context.Response.StatusCode = 204;
                        return;
                    }
                    if (path != "/target") { context.Response.StatusCode = 404; return; }
                }
                else if (path is not ("/" or "/viewer.js" or "/viewer.css" or "/stream"))
                { context.Response.StatusCode = 404; return; }
                if (path == "/stream")
                {
                    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
                    using var upstream = new ClientWebSocket();
                    upstream.Options.SetRequestHeader("Cookie", session.Cookie);
                    upstream.Options.SetRequestHeader("Origin", session.Http.BaseAddress!.GetLeftPart(UriPartial.Authority));
                    var socketUri = new UriBuilder(session.Http.BaseAddress!) { Scheme = "ws", Path = "/stream" }.Uri;
                    await upstream.ConnectAsync(socketUri, context.RequestAborted);
                    using var downstream = await context.WebSockets.AcceptWebSocketAsync();
                    using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                    var toWorker = CopyAsync(downstream, upstream, cancelled.Token);
                    var toViewer = CopyAsync(upstream, downstream, cancelled.Token);
                    await Task.WhenAny(toWorker, toViewer);
                    await cancelled.CancelAsync();
                    try { await Task.WhenAll(toWorker, toViewer); }
                    catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
                    return;
                }
                using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);
                request.Headers.Add("Origin", session.Http.BaseAddress!.GetLeftPart(UriPartial.Authority));
                using var response = await session.Http.SendAsync(request, context.RequestAborted);
                context.Response.StatusCode = (int)response.StatusCode;
                foreach (var header in new[] { "Content-Security-Policy", "X-Content-Type-Options", "Referrer-Policy" })
                    if (response.Headers.TryGetValues(header, out var values)) context.Response.Headers[header] = values.ToArray();
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or ObjectDisposedException
                or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            {
                logger.LogWarning("Local browser operation failed ({ErrorType}).", ex.GetType().Name);
                if (!context.Response.HasStarted)
                { context.Response.StatusCode = 502; await context.Response.WriteAsync("Browser operation failed. Check the local worker and saved-login directory, then retry. Return to the event if the session ended."); }
            }
        }).RequireAuthorization("OrganizerOnly").DisableAntiforgery();
    }

    private static async Task CopyAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await source.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (message.MessageType == WebSocketMessageType.Close) return;
            await destination.SendAsync(buffer.AsMemory(0, message.Count), message.MessageType, message.EndOfMessage, cancellationToken);
        }
    }
}
