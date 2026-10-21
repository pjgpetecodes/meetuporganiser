using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetupOrganiser.Tests;

public class ExtensionApiTests
{
    [Fact]
    public async Task Api_RequiresExplicitGrantSupportsPagingAndRevokesAccess()
    {
        var fixture = await BookingFixture.CreateAsync();
        for (var i = 0; i < 22; i++) await fixture.AddDateAsync();
        Guid eventId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.MeetupGroups.SingleAsync()).MeetupUrlName = "test-group";
            var meetup = await db.MeetupEvents.FirstAsync();
            eventId = meetup.Id;
            meetup.Title = "Saved event title";
            db.EventSpeakerSessions.Add(new() { MeetupEventId = meetup.Id, Speaker = new() { Name = "Public speaker", Bio = "Public bio",
                Email = "private-speaker@example.test", WebsiteUrl = "https://speaker.example" } });
            await db.SaveChangesAsync();
        }
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ExtensionAccess>();
        builder.Services.AddSingleton<IDbContextFactory<AppDbContext>>(fixture.Factory);
        builder.Services.AddSingleton(DispatchProxy.Create<IMeetupService, ApiDependency>());
        builder.Services.AddSingleton(DispatchProxy.Create<ISocialImageGeneratorService, ApiDependency>());
        builder.Services.AddSingleton(DispatchProxy.Create<ISettingsService, ApiDependency>());
        builder.Services.AddRateLimiter(options =>
        {
            foreach (var name in new[] { "extension-pairing", "extension-read" })
                options.AddConcurrencyLimiter(name, limiter => { limiter.PermitLimit = 5; limiter.QueueLimit = 0; });
        });
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapExtensionApi();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var unauthorised = await client.GetAsync("/api/extension/events/");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorised.StatusCode);
        var verifier = ExtensionAccessTests.Verifier();
        var started = await client.PostAsJsonAsync("/api/extension/pairing/start",
            new { challenge = ExtensionAccessTests.Challenge(verifier) });
        var start = await started.Content.ReadFromJsonAsync<JsonElement>();
        var id = start.GetProperty("requestId").GetString()!;
        Assert.True(app.Services.GetRequiredService<ExtensionAccess>().Approve(id, "organiser"));
        var redeemed = await client.PostAsJsonAsync("/api/extension/pairing/redeem", new { requestId = id, verifier });
        var grant = await redeemed.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", grant.GetProperty("token").GetString());
        var response = await client.GetAsync("/api/extension/events/?page=1");
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var events = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, events.GetProperty("events").GetArrayLength());
        Assert.True(events.GetProperty("hasMore").GetBoolean());
        var second = await client.GetFromJsonAsync<JsonElement>("/api/extension/events/?page=2");
        Assert.Equal(2, second.GetProperty("events").GetArrayLength());
        var searched = await client.GetFromJsonAsync<JsonElement>("/api/extension/events/?search=nonexistent");
        Assert.Empty(searched.GetProperty("events").EnumerateArray());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/extension/events/{eventId}");
        Assert.Equal("Saved event title", detail.GetProperty("title").GetString());
        Assert.Equal("Saved publication description", detail.GetProperty("description").GetString());
        Assert.Equal("test-group", detail.GetProperty("groupSlug").GetString());
        var speaker = detail.GetProperty("speaker");
        Assert.Equal("Public speaker", speaker.GetProperty("name").GetString());
        Assert.Equal("Public bio", speaker.GetProperty("bio").GetString());
        Assert.False(speaker.TryGetProperty("email", out _));
        Assert.False(speaker.TryGetProperty("id", out _));
        Assert.Equal(1, detail.GetProperty("speakerCount").GetInt32());
        Assert.False(detail.TryGetProperty("email", out _));
        Assert.False(detail.TryGetProperty("token", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/extension/events/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/extension/events/disconnect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/extension/events/")).StatusCode);
        await app.StopAsync();
    }

    public class ApiDependency : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            "GetSettingAsync" => Task.FromResult<string?>(null),
            "GenerateMeetupDescription" => "Saved publication description",
            "GenerateSocialCardAsync" => Task.FromResult<byte[]>([137, 80, 78, 71, 13, 10, 26, 10, 0]),
            _ => throw new NotSupportedException(targetMethod.Name)
        };
    }
}
