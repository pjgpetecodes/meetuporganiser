using MeetupOrganiser.Infrastructure.Services;
using Xunit;

namespace MeetupOrganiser.Tests;

public class SocialSlideSelectionTests
{
    private static (int Number, IReadOnlyList<string> Tokens) Slide(int n, params string[] tokens) => (n, tokens);

    [Fact]
    public void PicksTheSlideShowingTheEventDate()
    {
        var slides = new[] { Slide(1, "{{SpeakerName}}"), Slide(2, "{{EventDateLine}}", "{{SpeakerName}}"), Slide(3, "{{NextEventDate}}") };
        Assert.Equal(2, PptxTokens.FindSocialSlide(slides, 1));
    }

    [Fact]
    public void PrefersTheVariantMatchingSpeakerCount()
    {
        var slides = new[] { Slide(1, "{{EventDate}}", "{{SpeakerName}}"), Slide(2, "{{EventDate}}", "{{Speaker2Name}}") };
        Assert.Equal(1, PptxTokens.FindSocialSlide(slides, 1));
        Assert.Equal(2, PptxTokens.FindSocialSlide(slides, 2));
    }

    [Fact]
    public void ReturnsNullWithoutADateSlide()
    {
        Assert.Null(PptxTokens.FindSocialSlide(new[] { Slide(1, "{{SpeakerName}}"), Slide(2, "{{NextEventDate}}") }, 1));
    }
}
