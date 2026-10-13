using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Services;

namespace MeetupOrganiser.Tests;

public class SessionizeProfileTests
{
    private const string SearchHtml = """
        <div class="c-entry c-entry--speaker">
          <div class="c-entry__intro">
            <div class="c-entry__media"><img src="https://cdn.sessionize.com/image/a.jpg" alt="Luce Carter"></div>
            <h3 class="c-entry__title"><a href="/luce-carter"> Luce Carter </a></h3>
            <p class="c-entry__tagline"> Microsoft MVP. </p>
          </div>
        </div>
        <div class="c-entry c-entry--speaker">
          <h3 class="c-entry__title"><a href="https://evil.example/x">Bad</a></h3>
        </div>
        """;

    private const string ProfileHtml = """
        <meta property="og:image" content="https://cdn.sessionize.com/image/og.jpg" />
        <figure class="c-s-speaker-info__avatar"><img src="https://cdn.sessionize.com/image/p.jpg" alt="Luce" /></figure>
        <h1 class="c-s-speaker-info__name">Luce Carter</h1>
        <p class="c-s-speaker-info__tagline">Lover of code.</p>
        <p class="c-s-speaker-info__location">Manchester, United Kingdom</p>
        <div class="c-s-speaker-info__bio"><p>Luce is an MVP.<br /><br />She speaks a lot.</p></div>
        <div class="c-s-speaker-info__group c-s-speaker-info__group--links">
          <ul><li><a class="c-s-links__link" href="https://twitter.com/LuceCarter1"><span>@LuceCarter1</span></a></li>
              <li><a class="c-s-links__link" href="https://www.lucecarter.co.uk"><span>Blog</span></a></li></ul>
        </div>
        <div class="c-s-session">
          <h3 class="c-s-session__title"><a href="/s/luce-carter/zero/48077">Zero to Hero</a></h3>
          <p class="c-s-session__summary">Learn it.<br /><br />Enjoy.</p>
        </div>
        """;

    [Fact]
    public void ParseSearchResults_ReadsCandidatesAndSkipsForeignLinks()
    {
        var results = SessionizeProfileParser.ParseSearchResults(SearchHtml);

        var candidate = Assert.Single(results);
        Assert.Equal("luce-carter", candidate.Slug);
        Assert.Equal("Luce Carter", candidate.Name);
        Assert.Equal("Microsoft MVP.", candidate.Tagline);
        Assert.Equal("https://cdn.sessionize.com/image/a.jpg", candidate.PhotoUrl);
    }

    [Fact]
    public void ParseProfile_ReadsAllFields()
    {
        var profile = SessionizeProfileParser.ParseProfile(ProfileHtml, "luce-carter");

        Assert.NotNull(profile);
        Assert.Equal("Luce Carter", profile.Name);
        Assert.Equal("Luce is an MVP.\n\nShe speaks a lot.", profile.Bio);
        Assert.Equal("https://cdn.sessionize.com/image/p.jpg", profile.PhotoUrl);
        Assert.Equal(2, profile.Links.Count);
        var session = Assert.Single(profile.Sessions);
        Assert.Equal("Zero to Hero", session.Title);
        Assert.Equal("https://sessionize.com/s/luce-carter/zero/48077", session.Url);
    }

    [Fact]
    public void ParseProfile_ReturnsNullWhenNoName() =>
        Assert.Null(SessionizeProfileParser.ParseProfile("<html></html>", "x1"));

    [Theory]
    [InlineData("luce-carter", "luce-carter")]
    [InlineData("https://sessionize.com/Luce-Carter/", "luce-carter")]
    [InlineData("sessionize.com/luce-carter", "luce-carter")]
    [InlineData("https://www.sessionize.com/luce-carter?x=1", "luce-carter")]
    [InlineData("https://evil.com/luce-carter", null)]
    [InlineData("http://sessionize.com/luce-carter", null)]
    [InlineData("https://sessionize.com.evil.com/luce", null)]
    [InlineData("speakers", null)]
    [InlineData("../etc", null)]
    [InlineData("", null)]
    public void TryGetSlug_ValidatesHostAndSlug(string input, string? expected) =>
        Assert.Equal(expected, SessionizeProfileUrl.TryGetSlug(input));

    [Fact]
    public void ApplyProfile_FillsBlankNameSoBioUsesIt()
    {
        var profile = SessionizeProfileParser.ParseProfile(ProfileHtml, "luce-carter")!;
        var speaker = new Speaker();

        SessionizeSpeakerMapper.ApplyProfile(speaker, profile, SessionizeProfileFields.Bio);

        Assert.Equal("Luce Carter", speaker.Name);
        Assert.DoesNotContain("They", speaker.BioThirdPerson);
    }

    [Fact]
    public void ApplyProfile_AppliesOnlySelectedFields()
    {
        var profile = SessionizeProfileParser.ParseProfile(ProfileHtml, "luce-carter")!;
        var speaker = new Speaker { Name = "Luce Carter", Bio = "Old", PhotoBlobUrl = "https://old/x.jpg", Pronouns = "she/her" };

        SessionizeSpeakerMapper.ApplyProfile(speaker, profile, SessionizeProfileFields.Bio | SessionizeProfileFields.Links);

        Assert.StartsWith("Luce is an MVP.", speaker.Bio);
        Assert.Equal("https://old/x.jpg", speaker.PhotoBlobUrl);
        Assert.Equal("@LuceCarter1", speaker.TwitterHandle);
        Assert.Equal("https://www.lucecarter.co.uk/", speaker.WebsiteUrl);
        Assert.False(string.IsNullOrWhiteSpace(speaker.BioThirdPerson));
    }
}
