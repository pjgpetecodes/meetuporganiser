using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeetupOrganiser.Tests;

public class MagicLinkServiceTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateTokenAsync_GeneratesValidToken_And_PersistsHashedEntity()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new MagicLinkService(context);
        var speakerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Act
        var (rawToken, tokenEntity) = await service.CreateTokenAsync(speakerId, sessionId, TimeSpan.FromDays(7));

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        Assert.NotNull(tokenEntity);
        Assert.Equal(speakerId, tokenEntity.SpeakerId);
        Assert.Equal(sessionId, tokenEntity.EventSpeakerSessionId);
        Assert.NotEqual(rawToken, tokenEntity.TokenHash); // Raw token is NOT stored plain in DB
        Assert.True(tokenEntity.ExpiresAtUtc > DateTime.UtcNow);
        Assert.False(tokenEntity.IsRevoked);
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidRawToken_ReturnsEntity()
    {
        // Arrange
        using var context = CreateDbContext();
        var speaker = new Speaker { Id = Guid.NewGuid(), Name = "Speaker Test" };
        var group = new MeetupGroup { Id = Guid.NewGuid(), Name = "Group Test" };
        var evt = new MeetupEvent { Id = Guid.NewGuid(), Title = "Event Test", MeetupGroupId = group.Id };
        var session = new EventSpeakerSession { Id = Guid.NewGuid(), SpeakerId = speaker.Id, MeetupEventId = evt.Id, TalkTitle = "Talk" };

        await context.Speakers.AddAsync(speaker);
        await context.MeetupGroups.AddAsync(group);
        await context.MeetupEvents.AddAsync(evt);
        await context.EventSpeakerSessions.AddAsync(session);
        await context.SaveChangesAsync();

        var service = new MagicLinkService(context);
        var (rawToken, tokenEntity) = await service.CreateTokenAsync(speaker.Id, session.Id);

        // Act
        var validated = await service.ValidateTokenAsync(rawToken);

        // Assert
        Assert.NotNull(validated);
        Assert.Equal(tokenEntity.Id, validated.Id);
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new MagicLinkService(context);
        var (rawToken, tokenEntity) = await service.CreateTokenAsync(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(-1));

        // Act
        var validated = await service.ValidateTokenAsync(rawToken);

        // Assert
        Assert.Null(validated);
    }

    [Fact]
    public async Task MarkTokenUsedAsync_MarksTokenAndPreventsReuse()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new MagicLinkService(context);
        var (rawToken, tokenEntity) = await service.CreateTokenAsync(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var marked = await service.MarkTokenUsedAsync(tokenEntity.Id);
        var secondValidation = await service.ValidateTokenAsync(rawToken);

        // Assert
        Assert.True(marked);
        Assert.Null(secondValidation); // Token cannot be reused once used
    }
}
