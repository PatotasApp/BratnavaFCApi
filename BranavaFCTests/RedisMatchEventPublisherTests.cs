using System.Text.Json;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class RedisMatchEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_WhenRedisUnavailable_ShouldPersistReplayEventOutbox()
    {
        await using var db = DbContextFactory.Create(nameof(PublishAsync_WhenRedisUnavailable_ShouldPersistReplayEventOutbox));
        var redis = new Mock<IRedisConnectionProvider>();
        redis.Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((StackExchange.Redis.IConnectionMultiplexer?)null);

        var sut = new RedisMatchEventPublisher(
            redis.Object,
            db,
            Mock.Of<ILogger<RedisMatchEventPublisher>>(),
            "replay_events");

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var eventTime = new DateTimeOffset(2026, 7, 21, 21, 0, 0, TimeSpan.FromHours(-3));

        var result = await sut.PublishAsync(
            groupId,
            matchId,
            MatchEventType.GolTimeA,
            secondsBeforeStart: 8,
            durationSeconds: 20,
            eventTime);

        result.Should().StartWith("db:");

        var outbox = await db.ReplayEventOutbox.SingleAsync();
        outbox.StreamKey.Should().Be("replay_events");
        outbox.GroupId.Should().Be(groupId);
        outbox.MatchId.Should().Be(matchId);
        outbox.Type.Should().Be(MatchEventType.GolTimeA);
        outbox.EventTime.Should().Be(eventTime);
        outbox.SecondsBeforeStart.Should().Be(8);
        outbox.DurationSeconds.Should().Be(20);
        outbox.Status.Should().Be("Pending");
        outbox.Reason.Should().Contain("Redis");

        using var payload = JsonDocument.Parse(outbox.Payload);
        payload.RootElement.GetProperty("groupId").GetGuid().Should().Be(groupId);
        payload.RootElement.GetProperty("matchId").GetGuid().Should().Be(matchId);
        payload.RootElement.GetProperty("type").GetString().Should().Be("GolTimeA");
        payload.RootElement.GetProperty("secondsBeforeStart").GetInt32().Should().Be(8);
        payload.RootElement.GetProperty("durationSeconds").GetInt32().Should().Be(20);

        using var streamFields = JsonDocument.Parse(outbox.StreamFieldsJson);
        streamFields.RootElement.GetProperty("payload").GetString().Should().Be(outbox.Payload);
        streamFields.RootElement.GetProperty("groupId").GetString().Should().Be(groupId.ToString());
        streamFields.RootElement.GetProperty("matchId").GetString().Should().Be(matchId.ToString());
        streamFields.RootElement.GetProperty("type").GetString().Should().Be("GolTimeA");
        streamFields.RootElement.GetProperty("eventTime").GetString().Should().Be(eventTime.ToString("O"));
        streamFields.RootElement.GetProperty("secondsBeforeStart").GetInt32().Should().Be(8);
        streamFields.RootElement.GetProperty("durationSeconds").GetInt32().Should().Be(20);
    }
}
