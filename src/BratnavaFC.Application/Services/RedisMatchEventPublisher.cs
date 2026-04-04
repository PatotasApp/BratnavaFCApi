using System.Text.Json;
using System.Text.Json.Serialization;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using StackExchange.Redis;

namespace BratnavaFC.Application.Services;

public class RedisMatchEventPublisher : IMatchEventPublisher
{
    private readonly IConnectionMultiplexer _redis;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        Converters          = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RedisMatchEventPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task PublishAsync(Guid groupId, Guid matchId, MatchEventType type, int durationSeconds, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            groupId,
            matchId,
            type,
            durationSeconds,
        }, _jsonOpts);

        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(RedisChannel.Literal("replay_events"), payload);
    }
}
