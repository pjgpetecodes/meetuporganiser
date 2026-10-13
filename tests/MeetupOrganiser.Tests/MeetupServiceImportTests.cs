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

        Assert.True(result.Success);
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
            """{"errors":[{"message":"Not authorized"}],"data":null}"""
        ]);
        var service = CreateService(handler);

        var result = await service.ImportPastEventsAsync("dotnetnotts");

        Assert.False(result.Success);
        Assert.Empty(result.Events);
        Assert.Contains("Not authorized", result.ErrorMessage);
    }

    private static MeetupService CreateService(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Meetup:ApiKey"] = "test-token"
            })
            .Build();
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
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }
}
