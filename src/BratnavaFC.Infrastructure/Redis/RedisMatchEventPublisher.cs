using System.Text.Json;
using System.Text.Json.Serialization;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BratnavaFC.Infrastructure.Redis;

public class RedisMatchEventPublisher : IMatchEventPublisher
{
    private readonly IRedisConnectionProvider _redis;
    private readonly AppDbContext _db;
    private readonly ILogger<RedisMatchEventPublisher> _logger;
    private readonly string _streamKey;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RedisMatchEventPublisher(
        IRedisConnectionProvider redis,
        AppDbContext db,
        ILogger<RedisMatchEventPublisher> logger,
        string streamKey = "replay_events")
    {
        _redis = redis;
        _db = db;
        _logger = logger;
        _streamKey = string.IsNullOrWhiteSpace(streamKey) ? "replay_events" : streamKey;
    }

    public async Task<string> PublishAsync(Guid groupId, Guid matchId, MatchEventType type, int secondsBeforeStart, int durationSeconds, DateTimeOffset? eventTime = null, CancellationToken ct = default)
    {
        var replayEventTime = (eventTime ?? DateTimeOffset.UtcNow).ToUniversalTime();

        var payload = JsonSerializer.Serialize(new
        {
            groupId,
            matchId,
            type,
            eventTime = replayEventTime,
            secondsBeforeStart,
            durationSeconds,
        }, _jsonOpts);

        var streamFieldsJson = JsonSerializer.Serialize(new
        {
            payload,
            groupId = groupId.ToString(),
            matchId = matchId.ToString(),
            type = type.ToString(),
            eventTime = replayEventTime.ToString("O"),
            secondsBeforeStart,
            durationSeconds,
        }, _jsonOpts);

        // Redis temporariamente desativado por limite do plano.
        // Quando voltar, reativar o StreamAddAsync abaixo e manter o outbox como fallback.
        return await SaveOutboxAsync(
            groupId,
            matchId,
            type,
            replayEventTime,
            secondsBeforeStart,
            durationSeconds,
            payload,
            streamFieldsJson,
            "Redis temporariamente desativado; evento salvo no banco.",
            ct);

        /*
        try
        {
            var redis = await _redis.GetConnectionAsync(ct);
            if (redis is null)
                return await SaveOutboxAsync(groupId, matchId, type, replayEventTime, secondsBeforeStart, durationSeconds, payload, streamFieldsJson, "Redis indisponivel.", ct);

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
            DependencyStatusMonitor.RecordWarning(
                "redis",
                $"Falha ao publicar evento de replay no stream '{_streamKey}'.",
                ex);

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

            return await SaveOutboxAsync(
                groupId,
                matchId,
                type,
                replayEventTime,
                secondsBeforeStart,
                durationSeconds,
                payload,
                streamFieldsJson,
                ex.Message,
                ct);
        }
        */
    }

    private async Task<string> SaveOutboxAsync(
        Guid groupId,
        Guid matchId,
        MatchEventType type,
        DateTimeOffset eventTime,
        int secondsBeforeStart,
        int durationSeconds,
        string payload,
        string streamFieldsJson,
        string reason,
        CancellationToken ct)
    {
        var outbox = new ReplayEventOutboxEntity(
            _streamKey,
            groupId,
            matchId,
            type,
            eventTime,
            secondsBeforeStart,
            durationSeconds,
            payload,
            streamFieldsJson,
            reason);

        _db.ReplayEventOutbox.Add(outbox);
        await _db.SaveChangesAsync(ct);

        try
        {
            _logger.LogWarning(
                "[ReplayOutbox] Evento salvo no banco porque Redis indisponivel. OutboxId={OutboxId} Stream={Stream} Group={GroupId} Match={MatchId} Type={Type}",
                outbox.Id,
                _streamKey,
                groupId,
                matchId,
                type);
        }
        catch { }

        return $"db:{outbox.Id}";
    }
}
