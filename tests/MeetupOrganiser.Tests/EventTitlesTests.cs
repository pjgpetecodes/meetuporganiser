using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using Xunit;

namespace MeetupOrganiser.Tests;

public class EventTitlesTests
{
    [Fact]
    public void Placeholder_IsDetected_AndReplacedFromSessions()
    {
        var title = EventTitles.Placeholder("Dotnet Notts", new DateTime(2026, 11, 5, 18, 30, 0));

        Assert.True(EventTitles.IsPlaceholder(title));
        Assert.False(EventTitles.IsPlaceholder("Cloud Native - Jane"));

        var sessions = new[]
        {
            new EventSpeakerSession { OrderIndex = 1, TalkTitle = "B", Speaker = new Speaker { Name = "Bob" } },
            new EventSpeakerSession { OrderIndex = 0, TalkTitle = "A", Speaker = new Speaker { Name = "Ann" } },
            new EventSpeakerSession { OrderIndex = 2, TalkTitle = "", Speaker = new Speaker { Name = "Skip" } }
        };
        Assert.Equal("A - Ann | B - Bob", EventTitles.FromSessions(sessions));
        Assert.Null(EventTitles.FromSessions([]));
    }
}
