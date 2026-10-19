using MeetupOrganiser.Web;

namespace MeetupOrganiser.Tests;

public class MeetupDateTimeTests
{
    [Theory]
    [InlineData(2027, 1, 18, 18, "GMT")]
    [InlineData(2027, 7, 19, 17, "BST")]
    public void UkInput_UsesUkOffsetRatherThanServerTimezone(int year, int month, int day, int utcHour, string zone)
    {
        var local = new DateTime(year, month, day, 18, 30, 0, DateTimeKind.Unspecified);
        var utc = MeetupDateTime.ToUtc(local);

        Assert.Equal(new DateTime(year, month, day, utcHour, 30, 0, DateTimeKind.Utc), utc);
        Assert.Contains($"18:30 {zone}", MeetupDateTime.Display(utc));
    }

    [Theory]
    [InlineData(2027, 3, 28)]
    [InlineData(2027, 10, 31)]
    public void ClockChangeAmbiguity_IsRejected(int year, int month, int day) =>
        Assert.Throws<ArgumentException>(() => MeetupDateTime.ToUtc(new DateTime(year, month, day, 1, 30, 0)));
}
