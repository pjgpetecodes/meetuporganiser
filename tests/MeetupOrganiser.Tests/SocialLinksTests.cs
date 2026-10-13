using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class SocialLinksTests
{
    [Theory]
    [InlineData("lucecarter.bsky.social", "https://bsky.app/profile/lucecarter.bsky.social")]
    [InlineData("@lucecarter.bsky.social", "https://bsky.app/profile/lucecarter.bsky.social")]
    [InlineData("https://bsky.app/profile/onlymollyknows.bsky.social", "https://bsky.app/profile/onlymollyknows.bsky.social")]
    public void BlueSkyUrl_BuildsProfileLink(string value, string expected) =>
        Assert.Equal(expected, SocialLinks.BlueSkyUrl(value));

    [Theory]
    [InlineData("@madSimonJ", "https://x.com/madSimonJ", "@madSimonJ")]
    [InlineData("madSimonJ", "https://x.com/madSimonJ", "@madSimonJ")]
    [InlineData("https://twitter.com/madSimonJ", "https://twitter.com/madSimonJ", "@madSimonJ")]
    public void XLinks_AreNormalised(string value, string url, string handle)
    {
        Assert.Equal(url, SocialLinks.XUrl(value));
        Assert.Equal(handle, SocialLinks.XHandle(value));
    }

    [Fact]
    public void BlueSkyHandle_StripsProfileUrl() =>
        Assert.Equal("onlymollyknows.bsky.social", SocialLinks.BlueSkyHandle("https://bsky.app/profile/onlymollyknows.bsky.social"));

    [Fact]
    public void GitHub_AcceptsHandleProfileAndRepo()
    {
        Assert.Equal("https://github.com/molly", SocialLinks.GitHubUrl("molly"));
        Assert.Equal("https://github.com/molly/repo", SocialLinks.GitHubUrl("https://github.com/molly/repo"));
        Assert.Equal("molly/repo", SocialLinks.GitHubLabel("https://github.com/molly/repo"));
        Assert.Null(SocialLinks.GitHubUrl("https://evil.example/molly"));
    }

    [Fact]
    public void WebUrl_AddsSchemeAndRejectsJunk()
    {
        Assert.Equal("https://example.com/", SocialLinks.WebUrl("example.com"));
        Assert.Null(SocialLinks.WebUrl("javascript:alert(1)"));
    }
}

public class SessionizeLinkMappingTests
{
    private static SessionizeSpeakerDto Dto(params SessionizeLinkDto[] links) =>
        new("1", "Molly", "Painter", "Molly Painter", null, null, null, [], links.ToList());

    [Fact]
    public void Apply_RoutesEachLinkToItsOwnField()
    {
        var speaker = new Speaker();
        SessionizeSpeakerMapper.Apply(speaker, Dto(
            new("X", "https://x.com/madSimonJ", "Twitter"),
            new("LinkedIn", "https://www.linkedin.com/in/simon", "LinkedIn"),
            new("Bluesky", "https://bsky.app/profile/onlymollyknows.bsky.social", "Other"),
            new("Facebook", "https://www.facebook.com/simon.painter.18/", "Facebook"),
            new("Blog", "https://mollyblog.example/", "Blog")));

        Assert.Equal("@madSimonJ", speaker.TwitterHandle);
        Assert.Equal("https://www.linkedin.com/in/simon", speaker.LinkedInUrl);
        Assert.Equal("onlymollyknows.bsky.social", speaker.BlueSkyHandle);
        Assert.Equal("https://www.facebook.com/simon.painter.18/", speaker.FacebookUrl);
        Assert.Equal("https://mollyblog.example/", speaker.WebsiteUrl);
    }

    [Fact]
    public void Apply_FacebookIsNotFiledAsWebsite()
    {
        var speaker = new Speaker();
        SessionizeSpeakerMapper.Apply(speaker, Dto(new SessionizeLinkDto("Personal", "https://www.facebook.com/x", "Facebook")));
        Assert.Null(speaker.WebsiteUrl);
        Assert.NotNull(speaker.FacebookUrl);
    }

    [Fact]
    public void Tidy_MovesMisfiledFacebookAndStripsBlueSkyUrl()
    {
        var speaker = new Speaker
        {
            WebsiteUrl = "https://www.facebook.com/simon.painter.18/",
            BlueSkyHandle = "https://bsky.app/profile/onlymollyknows.bsky.social"
        };

        SessionizeSpeakerMapper.TidySocialFields(speaker);

        Assert.Equal("https://www.facebook.com/simon.painter.18/", speaker.FacebookUrl);
        Assert.Null(speaker.WebsiteUrl);
        Assert.Equal("onlymollyknows.bsky.social", speaker.BlueSkyHandle);
    }
}
