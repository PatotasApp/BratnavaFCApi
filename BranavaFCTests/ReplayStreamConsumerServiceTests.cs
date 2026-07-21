using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BranavaFC.Tests;

public class ReplayStreamConsumerServiceTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ReplayStreamConsumerService CreateSut(IServiceScopeFactory scopeFactory)
    {
        var redis = new Mock<IRedisConnectionProvider>();
        var logger = new Mock<ILogger<ReplayStreamConsumerService>>();
        return new ReplayStreamConsumerService(redis.Object, scopeFactory, logger.Object);
    }

    private static Mock<IDatabase> CreateMockDb()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);
        return db;
    }

    private static (IServiceScopeFactory factory, AppDbContext context) CreateSuccessScope(string dbName)
    {
        var appDb = DbContextFactory.Create(dbName);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(p => p.GetService(typeof(AppDbContext)))
            .Returns(appDb);

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        return (mockScopeFactory.Object, appDb);
    }

    private static IServiceScopeFactory CreateFailingScope()
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(p => p.GetService(typeof(AppDbContext)))
            .Throws(new InvalidOperationException("Simulated DB failure"));

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        return mockScopeFactory.Object;
    }

    private static StreamEntry MakeEntry(
        string id,
        Guid groupId,
        Guid matchId,
        string tipo = "gol",
        string objectKey = "gols/test.mp4")
    {
        return new StreamEntry(id, new NameValueEntry[]
        {
            new("group_id",    groupId.ToString()),
            new("match_id",    matchId.ToString()),
            new("bucket_name", "goal-replays"),
            new("object_key",  objectKey),
            new("content_type","video/mp4"),
            new("etag",        "abc123etag"),
            new("created_at",  DateTimeOffset.UtcNow.ToString("O")),
            new("tipo",        tipo),
        });
    }

    // ─── HandleEntryAsync — success path ─────────────────────────────────────

    [Fact]
    public async Task HandleEntryAsync_ValidGolEntry_SavesClipToDb()
    {
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var (scopeFactory, appDb) = CreateSuccessScope(
            nameof(HandleEntryAsync_ValidGolEntry_SavesClipToDb));
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(scopeFactory);

        await sut.HandleEntryAsync(mockRedisDb.Object, MakeEntry("1-0", groupId, matchId, "gol"), CancellationToken.None);

        var clip = await appDb.ReplayClips.SingleAsync();
        clip.GroupId.Should().Be(groupId);
        clip.MatchId.Should().Be(matchId);
        clip.EventType.Should().Be(MatchEventType.Gol);
    }

    [Fact]
    public async Task HandleEntryAsync_ValidEntry_AcknowledgesMessage()
    {
        var (scopeFactory, _) = CreateSuccessScope(
            nameof(HandleEntryAsync_ValidEntry_AcknowledgesMessage));
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(scopeFactory);

        await sut.HandleEntryAsync(mockRedisDb.Object, MakeEntry("2-0", Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        mockRedisDb.Verify(
            d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleEntryAsync_ValidJogadaEntry_SetsEventTypeJogada()
    {
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var (scopeFactory, appDb) = CreateSuccessScope(
            nameof(HandleEntryAsync_ValidJogadaEntry_SetsEventTypeJogada));
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(scopeFactory);

        await sut.HandleEntryAsync(mockRedisDb.Object, MakeEntry("3-0", groupId, matchId, "jogada"), CancellationToken.None);

        var clip = await appDb.ReplayClips.SingleAsync();
        clip.EventType.Should().Be(MatchEventType.Jogada);
    }

    // ─── HandleEntryAsync — failure path ─────────────────────────────────────

    [Fact]
    public async Task HandleEntryAsync_OnFirstFailure_DoesNotAcknowledge()
    {
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(CreateFailingScope());

        await sut.HandleEntryAsync(mockRedisDb.Object, MakeEntry("10-0", Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        mockRedisDb.Verify(
            d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleEntryAsync_OnSecondFailure_DoesNotAcknowledge()
    {
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(CreateFailingScope());
        var entry = MakeEntry("20-0", Guid.NewGuid(), Guid.NewGuid());

        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);

        mockRedisDb.Verify(
            d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleEntryAsync_AfterMaxAttempts_AcknowledgesAndDiscards()
    {
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(CreateFailingScope());
        var entry = MakeEntry("30-0", Guid.NewGuid(), Guid.NewGuid());

        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);

        mockRedisDb.Verify(
            d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleEntryAsync_AfterMaxAttempts_SubsequentCallsDoNotAckAgain()
    {
        // After discard, failure count is cleared — next call starts fresh
        var mockRedisDb = CreateMockDb();
        var sut = CreateSut(CreateFailingScope());
        var entry = MakeEntry("40-0", Guid.NewGuid(), Guid.NewGuid());

        // First round: 3 failures → discarded (1 ACK)
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);

        // A 4th call (e.g., entry reappears from pending): new failure cycle, no new ACK yet
        await sut.HandleEntryAsync(mockRedisDb.Object, entry, CancellationToken.None);

        mockRedisDb.Verify(
            d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once); // still just the one from the first discard
    }

    // ─── HandleEntryAsync — unknown tipo ─────────────────────────────────────

    [Fact]
    public async Task HandleEntryAsync_UnknownTipo_TreatedAsFailure()
    {
        // ParseEventType throws ArgumentException for unknown tipo
        // The catch block handles it — no ACK on first failure
        var mockRedisDb = CreateMockDb();
        var (scopeFactory, _) = CreateSuccessScope(
            nameof(HandleEntryAsync_UnknownTipo_TreatedAsFailure));
        var sut = CreateSut(scopeFactory);

        await sut.HandleEntryAsync(
            mockRedisDb.Object,
            MakeEntry("50-0", Guid.NewGuid(), Guid.NewGuid(), tipo: "cartao"),
            CancellationToken.None);

        mockRedisDb.Verify(
            d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }
}
