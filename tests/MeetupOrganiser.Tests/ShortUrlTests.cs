using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class ShortUrlTests
{
    [Theory]
    [InlineData("https://bit.ly/dotnetnotts", "https://bit.ly/dotnetnottsoctober2026")]
    [InlineData("https://bit.ly/dotnetnotts/", "https://bit.ly/dotnetnottsoctober2026")]
    [InlineData("dotnetnotts", "https://bit.ly/dotnetnottsoctober2026")]
    public void ForEvent_AppendsMonthAndYear(string stem, string expected) =>
        Assert.Equal(expected, ShortUrl.ForEvent(stem, new DateTime(2026, 10, 27, 18, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void ForEvent_UsesUkLocalMonth() =>
        Assert.Equal("https://bit.ly/xjuly2026", ShortUrl.ForEvent("https://bit.ly/x", new DateTime(2026, 6, 30, 23, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void ForEvent_BlankStem_ReturnsNull() =>
        Assert.Null(ShortUrl.ForEvent("  ", DateTime.UtcNow));
}
