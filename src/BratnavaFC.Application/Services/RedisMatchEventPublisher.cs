using System.Text.Json;
using System.Text.Json.Serialization;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BratnavaFC.Application.Services;

public class RedisMatchEventPublisher : IMatchEventPublisher
{
    private readonly IRedisConnectionProvider _redis;
    private readonly ILogger<RedisMatchEventPublisher> _logger;
    private readonly string _streamKey;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeZoneInfo _saoPauloTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public RedisMatchEventPublisher(
        IRedisConnectionProvider redis,
        ILogger<RedisMatchEventPublisher> logger,
        string streamKey = "replay_events")
    {
        _redis = redis;
        _logger = logger;
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

        var redis = await _redis.GetConnectionAsync(ct)
            ?? throw new InvalidOperationException("Redis indisponível. Não foi possível publicar o evento de replay agora.");

        try
        {
            var db = redis.GetDatabase();
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
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            try
            {
                _logger.LogWarning(
                    ex,
                    "[Redis] Falha ao publicar evento de replay. Stream={Stream} Group={GroupId} Match={MatchId} Type={Type}",
                    _streamKey,
                    groupId,
                    matchId,
                    type);
            }
            catch { }

            throw new InvalidOperationException("Redis indisponível ou com limite excedido. Não foi possível publicar o evento de replay agora.", ex);
        }
    }
}
