using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class SocialMediaServiceTests
{
    private MeetupEvent CreateSampleEvent()
    {
        var group = new MeetupGroup
        {
            Id = Guid.NewGuid(),
            Name = ".NET Notts",
            DefaultHashtags = "@dotnetnotts #dotnet"
        };

        var speaker = new Speaker
        {
            Id = Guid.NewGuid(),
            Name = "Pete Gallagher",
            TwitterHandle = "pete_codes",
            LinkedInUrl = "https://linkedin.com/in/pjgpetecodes"
        };

        var evt = new MeetupEvent
        {
            Id = Guid.NewGuid(),
            MeetupGroup = group,
            MeetupGroupId = group.Id,
            Title = "Cloud-Native .NET 10",
            Description = "Join us for an exciting evening learning about .NET 10.",
            EventDateTimeUtc = new DateTime(2026, 11, 15, 18, 30, 0, DateTimeKind.Utc),
            DurationMinutes = 90,
            LocationOrPlatform = "Zoom Online",
            ShortBitlyUrl = "https://bit.ly/dotnetnotts-nov26",
            Status = EventStatus.Scheduled
        };

        evt.Sessions.Add(new EventSpeakerSession
        {
            Id = Guid.NewGuid(),
            MeetupEventId = evt.Id,
            Speaker = speaker,
            SpeakerId = speaker.Id,
            TalkTitle = "Zero to Hero with Azure & .NET",
            TalkDescription = "A deep dive into cloud architecture.",
            OrderIndex = 0
        });

        return evt;
    }

    [Fact]
    public void GeneratePlatformPostText_Twitter_IncludesHandleAndUrl()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var httpClient = new HttpClient();
        var service = new SocialMediaService(httpClient, config, NullLogger<SocialMediaService>.Instance);
        var evt = CreateSampleEvent();

        // Act
        var text = service.GeneratePlatformPostText(evt, "Twitter");

        // Assert
        Assert.Contains("@pete_codes", text);
        Assert.Contains("Zero to Hero", text);
        Assert.Contains("https://bit.ly/dotnetnotts-nov26", text);
    }

    [Fact]
    public void GetPlatformWebIntentUrl_Twitter_GeneratesValidIntentLink()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var httpClient = new HttpClient();
        var service = new SocialMediaService(httpClient, config, NullLogger<SocialMediaService>.Instance);
        var evt = CreateSampleEvent();

        // Act
        var intentUrl = service.GetPlatformWebIntentUrl(evt, "Twitter");

        // Assert
        Assert.StartsWith("https://twitter.com/intent/tweet?text=", intentUrl);
        Assert.Contains("pete_codes", intentUrl);
    }

    [Fact]
    public void GetPlatformWebIntentUrl_LinkedIn_GeneratesValidIntentLink()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var httpClient = new HttpClient();
        var service = new SocialMediaService(httpClient, config, NullLogger<SocialMediaService>.Instance);
        var evt = CreateSampleEvent();

        // Act
        var intentUrl = service.GetPlatformWebIntentUrl(evt, "LinkedIn");

        // Assert
        Assert.StartsWith("https://www.linkedin.com/feed/?shareActive=true&text=", intentUrl);
    }

    [Fact]
    public async Task PostToLinkedInAsync_WithMemberToken_PublishesTextPost()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
            Assert.Equal("202610", request.Headers.GetValues("Linkedin-Version").Single());
            Assert.Equal("2.0.0", request.Headers.GetValues("X-Restli-Protocol-Version").Single());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Headers = { { "x-restli-id", "urn:li:share:12345" } }
            });
        });
        var service = CreateLinkedInService(handler);

        var result = await service.PostToLinkedInAsync("Hello LinkedIn", null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("urn:li:share:12345", result.PostId);
        Assert.Equal("urn:li:person:member123", JsonDocument.Parse(handler.Bodies[0]!)
            .RootElement.GetProperty("author").GetString());
    }

    [Fact]
    public async Task PostToLinkedInAsync_WithImage_UploadsImageThenPublishesMediaPost()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new TestHttpMessageHandler(async (request, _) =>
        {
            requests.Add(request);
            if (request.RequestUri!.AbsolutePath.EndsWith("/rest/images", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"value":{"uploadUrl":"https://www.linkedin.com/dms-uploads/upload123","image":"urn:li:image:image123"}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Put)
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Equal("image/png", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal(new byte[] { 1, 2, 3 }, await request.Content.ReadAsByteArrayAsync());
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Headers = { { "x-restli-id", "urn:li:share:67890" } }
            };
        });
        var service = CreateLinkedInService(handler);

        var result = await service.PostToLinkedInAsync("Image post", [1, 2, 3]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, requests.Count);
        using var postBody = JsonDocument.Parse(handler.Bodies[2]!);
        Assert.Equal("urn:li:person:member123", postBody.RootElement.GetProperty("author").GetString());
        Assert.Equal("urn:li:image:image123", postBody.RootElement
            .GetProperty("content").GetProperty("media").GetProperty("id").GetString());
    }

    [Fact]
    public async Task PostToLinkedInAsync_WithoutAuthorUrn_ReturnsSetupError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:LinkedIn:AccessToken"] = "test-token"
            })
            .Build();
        var service = new SocialMediaService(new HttpClient(), config, NullLogger<SocialMediaService>.Instance);

        var result = await service.PostToLinkedInAsync("Hello LinkedIn", null);

        Assert.False(result.Success);
        Assert.Contains("personal author URN", result.ErrorMessage);
        Assert.NotNull(result.FallbackShareUrl);
    }

    private static SocialMediaService CreateLinkedInService(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:LinkedIn:AccessToken"] = "test-token",
                ["Integrations:LinkedIn:AuthorUrn"] = "urn:li:person:member123"
            })
            .Build();
        return new SocialMediaService(new HttpClient(handler), config, NullLogger<SocialMediaService>.Instance);
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return await sendAsync(request, cancellationToken);
        }
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
