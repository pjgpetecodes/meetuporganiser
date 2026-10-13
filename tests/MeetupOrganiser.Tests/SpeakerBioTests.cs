using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class SpeakerBioTests
{
    private static Speaker Molly() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Molly Smith",
        Pronouns = "she/her",
        Bio = "I've been working with .NET for years. I love my community."
    };

    private static MeetupEvent EventFor(Speaker speaker) => new()
    {
        Title = "Test Meetup",
        EventDateTimeUtc = new DateTime(2026, 1, 15, 18, 0, 0, DateTimeKind.Utc),
        Sessions =
        [
            new EventSpeakerSession { Speaker = speaker, SpeakerId = speaker.Id, TalkTitle = "Talk", OrderIndex = 1 }
        ]
    };

    private static MeetupService Service() =>
        new(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<MeetupService>.Instance);

    [Fact]
    public void Description_ConvertsTalkDescriptionToThirdPerson()
    {
        var speaker = Molly();
        var evt = EventFor(speaker);
        evt.Sessions.First().TalkDescription = "In this talk I will show how I build apps. You will learn my tips.";

        var description = Service().GenerateMeetupDescription(evt);

        Assert.Contains("In this talk Molly will show how she builds apps.", description);
        Assert.Contains("her tips", description);
        Assert.DoesNotContain("I will", description);
    }

    [Fact]
    public void Description_KeepsOriginalTalkDescriptionWhenPreferred()
    {
        var speaker = Molly();
        var evt = EventFor(speaker);
        evt.Sessions.First().TalkDescription = "In this talk I will show you.";

        var description = Service().GenerateMeetupDescription(evt, SpeakerBio.Original);

        Assert.Contains("In this talk I will show you.", description);
    }

    [Fact]
    public void Description_UsesThirdPersonBioByDefault()
    {
        var description = Service().GenerateMeetupDescription(EventFor(Molly()));

        Assert.Contains("Molly has been working", description);
        Assert.DoesNotContain("I've been working", description);
    }

    [Fact]
    public void Description_UsesStoredThirdPersonBioWhenPresent()
    {
        var speaker = Molly();
        speaker.BioThirdPerson = "Molly is a custom edited bio.";

        var description = Service().GenerateMeetupDescription(EventFor(speaker));

        Assert.Contains("Molly is a custom edited bio.", description);
    }

    [Fact]
    public void Description_HonoursOriginalPreference()
    {
        var description = Service().GenerateMeetupDescription(EventFor(Molly()), SpeakerBio.Original);

        Assert.Contains("I've been working", description);
    }

    [Fact]
    public void EnsureThirdPerson_FillsBlankButKeepsManualEditUnlessForced()
    {
        var speaker = Molly();
        SpeakerBio.EnsureThirdPerson(speaker);
        Assert.Contains("Molly has been working", speaker.BioThirdPerson);

        speaker.BioThirdPerson = "Manual";
        SpeakerBio.EnsureThirdPerson(speaker);
        Assert.Equal("Manual", speaker.BioThirdPerson);

        SpeakerBio.EnsureThirdPerson(speaker, force: true);
        Assert.Contains("Molly has been working", speaker.BioThirdPerson);
    }

    [Fact]
    public void SessionizeMapper_StoresBothBiosAndPronouns()
    {
        var dto = new SessionizeSpeakerDto(
            "1", "Molly", "Smith", "Molly Smith", "I am a developer.", null, null, [],
            QuestionAnswers: [new SessionizeQuestionAnswerDto("Preferred Pronoun", "she/her")]);
        var speaker = new Speaker();

        SessionizeSpeakerMapper.Apply(speaker, dto);

        Assert.Equal("she/her", speaker.Pronouns);
        Assert.Equal("I am a developer.", speaker.Bio);
        Assert.Equal("Molly is a developer.", speaker.BioThirdPerson);
    }
}
