using System.Security.Claims;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry();

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

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
