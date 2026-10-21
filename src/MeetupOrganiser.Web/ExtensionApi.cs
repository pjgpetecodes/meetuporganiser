using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Web;

public static class ExtensionApi
{
    public static void MapExtensionApi(this WebApplication app)
    {
        app.MapGet("/extension/authorize", (string? request) =>
            Results.LocalRedirect($"/extension/connect?request={Uri.EscapeDataString(request ?? "")}"))
            .RequireAuthorization("OrganizerOnly");
        var pairing = app.MapGroup("/api/extension/pairing").RequireRateLimiting("extension-pairing");
        pairing.MapPost("/start", (PairStart input, ExtensionAccess access, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var id = access.Start(input.Challenge ?? "");
            return id is null ? Results.BadRequest(new { error = "Invalid request or pairing capacity reached." })
                : Results.Ok(new { requestId = id, approvalPath = $"/extension/connect?request={id}" });
        });
        pairing.MapPost("/redeem", async (PairRedeem input, ExtensionAccess access, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await access.RedeemAsync(input.RequestId ?? "", input.Verifier ?? "", context.RequestAborted);
            return Results.Ok(new { status = result.Status, token = result.Token, expiresAt = result.ExpiresAt });
        });

        var api = app.MapGroup("/api/extension/events").RequireRateLimiting("extension-read").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            var header = http.Request.Headers.Authorization.ToString();
            var access = http.RequestServices.GetRequiredService<ExtensionAccess>();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || await access.OwnerAsync(header[7..], http.RequestAborted) is null)
                return Results.Unauthorized();
            return await next(context);
        });
        api.MapPost("/disconnect", async (HttpContext context, ExtensionAccess access) =>
        {
            await access.RevokeTokenAsync(context.Request.Headers.Authorization.ToString()[7..], context.RequestAborted);
            return Results.NoContent();
        });
        api.MapGet("/", async (string? search, int? page, IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
        {
            if (search?.Length > 200 || page is < 1 or > 10000) return Results.BadRequest();
            await using var db = await factory.CreateDbContextAsync(ct);
            var query = db.MeetupEvents.AsNoTracking().Where(e => e.EventDateTimeUtc > DateTime.UtcNow);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(e => e.Title.Contains(search) || e.MeetupGroup!.Name.Contains(search));
            var events = await query.OrderBy(e => e.EventDateTimeUtc).ThenBy(e => e.Id)
                .Skip(((page ?? 1) - 1) * 20).Take(21)
                .Select(e => new { e.Id, e.Title, group = e.MeetupGroup!.Name, e.EventDateTimeUtc })
                .ToListAsync(ct);
            return Results.Ok(new { events = events.Take(20), hasMore = events.Count > 20 });
        });
        api.MapGet("/{id:guid}", async (Guid id, IDbContextFactory<AppDbContext> factory, IMeetupService meetup,
            ISocialImageGeneratorService images, ISettingsService settings, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var e = await db.MeetupEvents.AsNoTracking().Include(e => e.MeetupGroup).ThenInclude(g => g!.Sponsors)
                .Include(e => e.Sessions).ThenInclude(s => s.Speaker).AsSplitQuery().SingleOrDefaultAsync(e => e.Id == id, ct);
            if (e is null) return Results.NotFound();
            var bio = !string.IsNullOrWhiteSpace(e.MeetupGroup?.BioPreference) ? e.MeetupGroup.BioPreference
                : await settings.GetSettingAsync(SpeakerBio.SettingKey, ct);
            var template = !string.IsNullOrWhiteSpace(e.MeetupGroup?.MeetupDescriptionTemplate) ? e.MeetupGroup.MeetupDescriptionTemplate
                : await settings.GetSettingAsync(MeetupDescriptionTemplate.SettingKey, ct) ?? MeetupDescriptionTemplate.Default;
            try
            {
                var sections = await db.TemplateSections.AsNoTracking().ToListAsync(ct);
                var description = meetup.GenerateMeetupDescription(e, bio, template, sections);
                var image = await images.GenerateSocialCardAsync(e, ct);
                var payload = MeetupWebsite.Prepare(e, e.Title, description, Convert.ToBase64String(image), bio);
                return Results.Ok(await MeetupWebsiteSpeakerPhoto.AttachAsync(payload, e, ct));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }

    public record PairStart(string? Challenge);
    public record PairRedeem(string? RequestId, string? Verifier);
}
