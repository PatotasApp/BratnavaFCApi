using System.Text.Json;
using System.Text.Json.Serialization;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using StackExchange.Redis;

namespace BratnavaFC.Application.Services;

public class RedisMatchEventPublisher : IMatchEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _streamKey;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeZoneInfo _saoPauloTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public RedisMatchEventPublisher(IConnectionMultiplexer redis, string streamKey = "replay_events")
    {
        _redis = redis;
        _streamKey = string.IsNullOrWhiteSpace(streamKey) ? "replay_events" : streamKey;
    }

    public async Task<string> PublishAsync(Guid groupId, Guid matchId, MatchEventType type, int secondsBeforeStart, int durationSeconds, DateTimeOffset? eventTime = null, CancellationToken ct = default)
    {
        var replayEventTime = TimeZoneInfo.ConvertTime(eventTime ?? DateTimeOffset.UtcNow, _saoPauloTz);

        var payload = JsonSerializer.Serialize(new
        {
            groupId,
            matchId,
            type,
            eventTime = replayEventTime,
            secondsBeforeStart,
            durationSeconds,
        }, _jsonOpts);

        var db = _redis.GetDatabase();
        var id = await db.StreamAddAsync(_streamKey, new[]
        {
            new NameValueEntry("payload", payload),
            new NameValueEntry("groupId", groupId.ToString()),
            new NameValueEntry("matchId", matchId.ToString()),
            new NameValueEntry("type", type.ToString()),
            new NameValueEntry("eventTime", replayEventTime.ToString("O")),
            new NameValueEntry("secondsBeforeStart", secondsBeforeStart),
            new NameValueEntry("durationSeconds", durationSeconds),
        });

        return id.ToString();
    }
}
