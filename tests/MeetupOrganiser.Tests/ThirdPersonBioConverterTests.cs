using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Tests;

public class ThirdPersonBioConverterTests
{
    [Theory]
    [InlineData("I've been working with .NET for 20 years.", "Molly", "she/her", "Molly has been working with .NET for 20 years.")]
    [InlineData("I am a developer. I love my job.", "Molly Smith", "she/her", "Molly is a developer. She loves her job.")]
    [InlineData("I'm a freelancer. I also teach and I enjoy coaching myself.", "Pete", "he/him", "Pete is a freelancer. He also teaches and he enjoys coaching himself.")]
    [InlineData("I work in Azure. My team helps me.", "Sam", "they/them", "Sam works in Azure. Their team helps them.")]
    [InlineData("I do a lot of talks and I have written books.", "Pete", "he/him", "Pete does a lot of talks and he has written books.")]
    [InlineData("I build apps. I try hard.", "Jo", "she/her", "Jo builds apps. She tries hard.")]
    [InlineData("I can't wait. I don't mind.", "Jo", "she/her", "Jo can't wait. She doesn't mind.")]
    [InlineData("I am a developer. I love my job and I help me.", "Molly Smith", null, "Molly is a developer. Molly loves Molly's job and Molly helps Molly.")]
    [InlineData("In this talk I will show how I build apps. My tips are mine.", "Molly", "", "In this talk Molly will show how Molly builds apps. Molly's tips are Molly's.")]
    public void Convert_RewritesFirstPersonToThirdPerson(string bio, string name, string pronouns, string expected) =>
        Assert.Equal(expected, ThirdPersonBioConverter.Convert(bio, name, pronouns));

    [Fact]
    public void Convert_UsesPluralVerbsForTheyAfterFirstSentence()
    {
        var result = ThirdPersonBioConverter.Convert("I am a developer. I am based in Nottingham. I have a dog.", "Sam", "they/them");

        Assert.Equal("Sam is a developer. They are based in Nottingham. They have a dog.", result);
    }

    [Fact]
    public void Convert_LeavesThirdPersonBioUnchanged()
    {
        const string bio = "Luce is a Senior Developer Advocate.\n\nShe loves learning.";

        Assert.Equal(bio, ThirdPersonBioConverter.Convert(bio, "Luce Carter", "she/her"));
    }

    [Fact]
    public void Convert_PreservesParagraphs()
    {
        var result = ThirdPersonBioConverter.Convert("I code.\n\nI teach.", "Jo", "she/her");

        Assert.Equal("Jo codes.\n\nShe teaches.", result);
    }
}
