using System.Net;
using System.Text;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class MeetupServiceImportTests
{
    [Fact]
    public async Task ImportPastEventsAsync_ImportsPagesAndExtractsSpeakers()
    {
        var handler = new QueueHttpMessageHandler(
        [
            """
            {
              "data": {
                "groupByUrlname": {
                  "events": {
                    "edges": [{
                      "node": {
                        "id": "event-1",
                        "title": "Building useful apps",
                        "description": "Our Speakers:\n\nAda Lovelace:\nA software pioneer.\n\nGrace Hopper:\nA computing pioneer.\n\nHow to find us:\nOnline",
                        "dateTime": "2023-05-10T18:30:00+01:00",
                        "duration": "PT1H45M",
                        "eventUrl": "https://www.meetup.com/example/events/event-1/",
                        "howToFindUs": "Online",
                        "speakerDetails": null,
                        "venue": { "name": "Nottingham" }
                      }
                    }],
                    "pageInfo": { "endCursor": "cursor-1", "hasNextPage": true }
                  }
                }
              }
            }
            """,
            """
            {
              "data": {
                "groupByUrlname": {
                  "events": {
                    "edges": [{
                      "node": {
                        "id": "event-2",
                        "title": "A meetup without speaker data",
                        "description": null,
                        "dateTime": "2022-02-01T19:00:00Z",
                        "duration": null,
                        "eventUrl": null,
                        "howToFindUs": null,
                        "speakerDetails": null,
                        "venue": null
                      }
                    }],
                    "pageInfo": { "endCursor": null, "hasNextPage": false }
                  }
                }
              }
            }
            """
        ]);
        var service = CreateService(handler);

        var result = await service.ImportPastEventsAsync("dotnetnotts");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("event-1", result.Events[0].MeetupEventId);
        Assert.Equal(DateTimeKind.Utc, result.Events[0].EventDateTimeUtc.Kind);
        Assert.Equal(new DateTime(2023, 5, 10, 17, 30, 0, DateTimeKind.Utc), result.Events[0].EventDateTimeUtc);
        Assert.Equal(105, result.Events[0].DurationMinutes);
        Assert.Equal("Nottingham, Online", result.Events[0].Location);
        Assert.Collection(
            result.Events[0].Speakers,
            speaker =>
            {
                Assert.Equal("Ada Lovelace", speaker.Name);
                Assert.Equal("A software pioneer.", speaker.Bio);
                Assert.Equal("Building useful apps", speaker.TalkTitle);
            },
            speaker =>
            {
                Assert.Equal("Grace Hopper", speaker.Name);
                Assert.Equal("A computing pioneer.", speaker.Bio);
            });
        Assert.Empty(result.Events[1].Speakers);
        Assert.Equal(90, result.Events[1].DurationMinutes);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
        });
        Assert.Contains("\"after\":\"cursor-1\"", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task ImportPastEventsAsync_ReturnsFailureWhenMeetupRejectsRequest()
    {
        var handler = new QueueHttpMessageHandler(
        [
            """{"errors":[{"message":"Not authorized"}],"data":null}""",
            "<html><body>No public past event links</body></html>"
        ]);
        var service = CreateService(handler);

        var result = await service.ImportPastEventsAsync("dotnetnotts");

        Assert.False(result.Success);
        Assert.Empty(result.Events);
        Assert.Contains("Not authorized", result.ErrorMessage);
    }

    [Theory]
    [InlineData("""<html><body><div>12 people going</div></body></html>""", 12)]
    [InlineData("""<html><body><div>1,234 attendees</div></body></html>""", 1234)]
    [InlineData("""<html><body><script type="application/json">{"event":{"attendeeCount":42}}</script></body></html>""", 42)]
    public async Task GetAttendeeCountAsync_ParsesAttendanceFromMeetupPage(string html, int expectedCount)
    {
        var service = new MeetupService(
            new HttpClient(new QueueHttpMessageHandler([html])),
            new ConfigurationBuilder().Build(),
            NullLogger<MeetupService>.Instance);

        var count = await service.GetAttendeeCountAsync("https://www.meetup.com/example/events/123/");

        Assert.Equal(expectedCount, count);
    }

    [Fact]
    public async Task GetAttendeeCountAsync_RejectsNonMeetupUrls()
    {
        var service = new MeetupService(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<MeetupService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAttendeeCountAsync("https://example.com/events/123/"));
    }

    [Fact]
    public async Task ImportPastEventsAsync_UsesHtmlFallbackWithoutApiCredentialsAndReadsMeetupSpeakerDetails()
    {
        var handler = new QueueHttpMessageHandler(
        [
            """
            <html><body>
              <a href="/example-group/events/event-123/">Past meetup</a>
            </body></html>
            """,
            "<html><body>No upcoming event links</body></html>",
            """
            <html><body>
              <script type="application/ld+json">
                {
                  "@context":"https://schema.org",
                  "@type":"Event",
                  "name":"Useful software",
                  "startDate":"2024-05-10T18:30:00+01:00",
                  "duration":"PT1H30M",
                  "description":"Our Speakers:\n\nAda Lovelace:\nA computing pioneer.\n\nHow to find us:\nNottingham",
                  "performer":{"@type":"Person","name":"Ada Lovelace","description":"A computing pioneer."},
                  "location":{"@type":"Place","name":"Nottingham"}
                }
              </script>
            </body></html>
            """
        ]);
        var service = CreateService(handler, configureToken: false);

        var result = await service.ImportPastEventsAsync("example-group");

        Assert.True(result.Success, result.ErrorMessage);
        var importedEvent = Assert.Single(result.Events);
        Assert.Equal("event-123", importedEvent.MeetupEventId);
        Assert.Equal("Useful software", importedEvent.Title);
        Assert.Equal(DateTimeKind.Utc, importedEvent.EventDateTimeUtc.Kind);
        Assert.Equal(90, importedEvent.DurationMinutes);
        Assert.Equal("Nottingham", importedEvent.Location);
        var speaker = Assert.Single(importedEvent.Speakers);
        Assert.Equal("Ada Lovelace", speaker.Name);
        Assert.Equal("A computing pioneer.", speaker.Bio);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task ImportPastEventsAsync_ReadsMeetupApolloDataFromPastAndUpcomingListingsButImportsOnlyPastEvents()
    {
        var handler = new QueueHttpMessageHandler(
        [
            """
            <html><body><script id="__NEXT_DATA__" type="application/json">
            {"props":{"pageProps":{"__APOLLO_STATE__":{
              "Event:100":{"__typename":"Event","id":"100","title":"Past meetup","eventUrl":"https://www.meetup.com/example-group/events/100/","description":"**Our Speaker:**\n\n**Ada Lovelace:**\n\nA computing pioneer.\n\n**How to find Ada:**\nhttps://example.com","dateTime":"2024-05-10T18:30:00+01:00","endTime":"2024-05-10T20:00:00+01:00","status":"PAST","venue":{"__ref":"Venue:1"}},
              "Venue:1":{"__typename":"Venue","name":"Nottingham"}
            }}}}
            </script></body></html>
            """,
            """
            <html><body><script id="__NEXT_DATA__" type="application/json">
            {"props":{"pageProps":{"__APOLLO_STATE__":{
              "Event:200":{"__typename":"Event","id":"200","title":"Upcoming meetup","eventUrl":"https://www.meetup.com/example-group/events/200/","description":"Future event","dateTime":"2099-05-10T18:30:00Z","endTime":"2099-05-10T20:00:00Z","status":"ACTIVE"}
            }}}}
            </script></body></html>
            """
        ]);
        var service = CreateService(handler, configureToken: false);

        var result = await service.ImportPastEventsAsync("example-group");

        Assert.True(result.Success, result.ErrorMessage);
        var importedEvent = Assert.Single(result.Events);
        Assert.Equal("100", importedEvent.MeetupEventId);
        Assert.Equal("Nottingham", importedEvent.Location);
        Assert.Equal(90, importedEvent.DurationMinutes);
        var speaker = Assert.Single(importedEvent.Speakers);
        Assert.Equal("Ada Lovelace", speaker.Name);
        Assert.Contains("A computing pioneer.", speaker.Bio);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task ImportPastEventsAsync_FallsBackToHtmlWhenApiFails()
    {
        var handler = new QueueHttpMessageHandler(
        [
            """{"errors":[{"message":"Not authorized"}],"data":null}""",
            """<html><body><a href="/example-group/events/event-456/">Past event</a></body></html>""",
            "<html><body>No upcoming event links</body></html>",
            """
            <html><body>
              <h1>Community evening</h1>
              <time datetime="2023-06-01T18:00:00Z"></time>
              <meta property="og:description" content="Our Speaker: Ada Lovelace" />
            </body></html>
            """
        ]);
        var service = CreateService(handler);

        var result = await service.ImportPastEventsAsync("example-group");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("event-456", Assert.Single(result.Events).MeetupEventId);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public void SessionizeSpeakerMapper_MapsProfileAndLinks()
    {
        var source = new MeetupOrganiser.Core.DTOs.SessionizeSpeakerDto(
            "1", "Ada", "Lovelace", "Ada Lovelace", "Bio text", "Tag",
            "https://sessionize.com/image/ada.jpg", [],
            [
                new("LinkedIn", "https://www.linkedin.com/in/ada", "LinkedIn"),
                new("Twitter", "https://twitter.com/ada", "Twitter"),
                new("Other", "https://bsky.app/profile/ada.bsky.social", "Other"),
                new("Other", "https://github.com/ada", "Other"),
                new("Blog", "https://ada.example/", "Blog")
            ]);
        var speaker = new MeetupOrganiser.Core.Entities.Speaker();

        MeetupOrganiser.Core.DTOs.SessionizeSpeakerMapper.Apply(speaker, source);

        Assert.Equal("Ada Lovelace", speaker.Name);
        Assert.Equal("Bio text", speaker.Bio);
        Assert.Equal("https://sessionize.com/image/ada.jpg", speaker.PhotoBlobUrl);
        Assert.Equal("https://www.linkedin.com/in/ada", speaker.LinkedInUrl);
        Assert.Equal("@ada", speaker.TwitterHandle);
        Assert.Equal("ada.bsky.social", speaker.BlueSkyHandle);
        Assert.Equal("ada", speaker.GitHubHandle);
        Assert.Equal("https://ada.example/", speaker.WebsiteUrl);
    }

    [Fact]
    public async Task SessionizeService_ParsesRealSpeakersPayload()
    {
        const string json = """
            [{"id":"876c9c7a","firstName":"Clyde","lastName":"Davies","fullName":"Clyde Davies",
              "bio":"Chemist","tagLine":"Leader","profilePicture":"https://cdn.sessionize.com/image/a.jpg",
              "sessions":[{"id":326103,"name":"Chem4Word"}],"isTopSpeaker":false,
              "links":[{"title":"LinkedIn","url":"https://www.linkedin.com/in/clyde/","linkType":"LinkedIn"},
                       {"title":"Sessionize","url":"https://sessionize.com/clyde-davies","linkType":"Sessionize"}],
              "questionAnswers":[],"categories":[]}]
            """;
        var service = new SessionizeService(
            new HttpClient(new QueueHttpMessageHandler([json])),
            NullLogger<SessionizeService>.Instance);

        var speakers = await service.GetSpeakersAsync("7y8ylyvc");

        var speaker = Assert.Single(speakers);
        var entity = new MeetupOrganiser.Core.Entities.Speaker();
        MeetupOrganiser.Core.DTOs.SessionizeSpeakerMapper.Apply(entity, speaker);
        Assert.Equal("Clyde Davies", entity.Name);
        Assert.Equal("https://www.linkedin.com/in/clyde/", entity.LinkedInUrl);
        Assert.Null(entity.WebsiteUrl);
    }

    private static MeetupService CreateService(HttpMessageHandler handler, bool configureToken = true)
    {
        var settings = new Dictionary<string, string?>();
        if (configureToken)
        {
            settings["Integrations:Meetup:ApiKey"] = "test-token";
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new MeetupService(
            new HttpClient(handler),
            configuration,
            NullLogger<MeetupService>.Instance);
    }

    private sealed class QueueHttpMessageHandler(IEnumerable<string> responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];
        private readonly Queue<string> _responses = new(responses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }
}
