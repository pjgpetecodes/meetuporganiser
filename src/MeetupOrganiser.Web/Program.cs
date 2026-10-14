using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Add Application Insights
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"])
    || !string.IsNullOrWhiteSpace(builder.Configuration["ApplicationInsights:ConnectionString"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// Add Infrastructure & EF Core Database
builder.Services.AddInfrastructureServices(builder.Configuration);

// Add Razor & Interactive Server Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services.AddCascadingAuthenticationState();

var azureAdConfig = builder.Configuration.GetSection("AzureAd");
var clientId = azureAdConfig["ClientId"];
var isEntraConfigured = !string.IsNullOrWhiteSpace(clientId) && !clientId.StartsWith("00000000");

if (isEntraConfigured)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(azureAdConfig);
}
else
{
    // Local / Dev Fallback Cookie Authentication
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.Cookie.Name = "MeetupOrganiser.Auth";
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrganizerOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
    });
});

var app = builder.Build();

// Auto-migrate & seed database on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsSqlServer())
        {
            try
            {
                await db.Database.MigrateAsync();
                await DbInitializer.SeedAsync(db, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not run automated migrations against SQL Server. Ensuring database created locally.");
                await db.Database.EnsureCreatedAsync();
                await DbInitializer.SeedAsync(db, logger);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize and seed database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();

// Serve local storage files if wwwroot/storage exists
var localStorageDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "storage");
if (Directory.Exists(localStorageDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(localStorageDir),
        RequestPath = "/storage"
    });
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

const string meetupOAuthStateCookie = "MeetupOAuthState";
app.MapGet("/integrations/meetup/connect", async (
    HttpContext context,
    IMeetupOAuthService meetupOAuthService,
    ILogger<Program> logger) =>
{
    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
    context.Response.Cookies.Append(meetupOAuthStateCookie, state, new CookieOptions
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/integrations/meetup"
    });

    try
    {
        return Results.Redirect(await meetupOAuthService.CreateAuthorizationUrlAsync(state, context.RequestAborted));
    }
    catch (InvalidOperationException ex)
    {
        logger.LogWarning(ex, "Meetup OAuth connection could not be started.");
        return Results.Redirect("/settings?meetup=error");
    }
}).RequireAuthorization("OrganizerOnly");

app.MapGet("/integrations/meetup/callback", async (
    HttpContext context,
    IMeetupOAuthService meetupOAuthService,
    ILogger<Program> logger,
    string? code,
    string? state,
    string? error) =>
{
    var expectedState = context.Request.Cookies[meetupOAuthStateCookie];
    context.Response.Cookies.Delete(meetupOAuthStateCookie, new CookieOptions
    {
        Path = "/integrations/meetup",
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax
    });

    if (string.IsNullOrWhiteSpace(expectedState)
        || string.IsNullOrWhiteSpace(state)
        || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedState),
            Encoding.UTF8.GetBytes(state)))
    {
        return Results.Redirect("/settings?meetup=state-error");
    }

    if (!string.IsNullOrWhiteSpace(error))
    {
        logger.LogInformation("Meetup OAuth authorization returned {OAuthError}.", error);
        return Results.Redirect("/settings?meetup=cancelled");
    }

    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.Redirect("/settings?meetup=error");
    }

    try
    {
        await meetupOAuthService.CompleteAuthorizationAsync(code, context.RequestAborted);
        return Results.Redirect("/settings?meetup=connected");
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
    {
        logger.LogError(ex, "Meetup OAuth authorization-code exchange failed.");
        return Results.Redirect("/settings?meetup=error");
    }
}).RequireAuthorization("OrganizerOnly");

// Developer Sign-In / Sign-Out endpoints for testing when Entra is in dev mode
app.MapGet("/login", async (HttpContext context) =>
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, "Pete Gallagher (Organizer)"),
        new(ClaimTypes.Email, "organizer@dotnetnotts.co"),
        new(ClaimTypes.Role, "Organizer"),
        new("preferred_username", "pete@petecodes.co.uk")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/dashboard");
});

if (!isEntraConfigured)
{
    app.MapGet("/MicrosoftIdentity/Account/SignIn", () => Results.Redirect("/login"));
}

app.MapGet("/logout", async (HttpContext context) =>
{
    if (isEntraConfigured)
    {
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// Minimal API endpoints for direct asset downloading
app.MapGet("/api/events/{id:guid}/pptx", async (Guid id, AppDbContext db, IPptxGeneratorService pptx) =>
{
    var evt = await db.MeetupEvents
        .Include(e => e.MeetupGroup)
        .Include(e => e.Sessions).ThenInclude(s => s.Speaker)
        .FirstOrDefaultAsync(e => e.Id == id);
    if (evt == null) return Results.NotFound();

    var bytes = await pptx.GenerateEventDeckAsync(evt);
    var safeName = string.Join("_", evt.Title.Split(Path.GetInvalidFileNameChars()));
    if (string.IsNullOrWhiteSpace(safeName)) safeName = "MeetupEvent";
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation", $"{safeName}.pptx");
});

app.MapGet("/api/events/{id:guid}/social-image", async (Guid id, AppDbContext db, ISocialImageGeneratorService socialImg) =>
{
    var evt = await db.MeetupEvents
        .Include(e => e.MeetupGroup)
        .Include(e => e.Sessions).ThenInclude(s => s.Speaker)
        .FirstOrDefaultAsync(e => e.Id == id);
    if (evt == null) return Results.NotFound();

    var bytes = await socialImg.GenerateSocialCardAsync(evt);
    var safeName = string.Join("_", evt.Title.Split(Path.GetInvalidFileNameChars()));
    if (string.IsNullOrWhiteSpace(safeName)) safeName = "MeetupEvent";
    return Results.File(bytes, "image/png", $"{safeName}-social.png");
});

app.MapGet("/api/templates/{file}/slides/{n:int}.png", async (string file, int n, int? w, IPptxPreviewService preview, CancellationToken ct) =>
{
    var png = await preview.RenderSlideAsync(file, n, Math.Clamp(w ?? 480, 120, 1280), ct);
    return png == null ? Results.NotFound() : Results.File(png, "image/png");
}).RequireAuthorization("OrganizerOnly");

app.MapGet("/api/templates/{file}/slides/{n:int}/filled.png", async (string file, int n, int? speakers, int? w, IPptxPreviewService preview, CancellationToken ct) =>
{
    var png = await preview.RenderFilledSlideAsync(file, n, speakers is > 1 ? 2 : 1, Math.Clamp(w ?? 960, 120, 1280), ct);
    return png == null ? Results.NotFound() : Results.File(png, "image/png");
}).RequireAuthorization("OrganizerOnly");

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
