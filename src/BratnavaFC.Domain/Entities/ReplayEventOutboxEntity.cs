using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public sealed class ReplayEventOutboxEntity : BaseEntity
{
    private ReplayEventOutboxEntity() { }

    public ReplayEventOutboxEntity(
        string streamKey,
        Guid groupId,
        Guid matchId,
        MatchEventType type,
        DateTimeOffset eventTime,
        int secondsBeforeStart,
        int durationSeconds,
        string payload,
        string streamFieldsJson,
        string reason)
    {
        StreamKey = string.IsNullOrWhiteSpace(streamKey) ? "replay_events" : streamKey.Trim();
        GroupId = groupId;
        MatchId = matchId;
        Type = type;
        EventTime = eventTime;
        SecondsBeforeStart = secondsBeforeStart;
        DurationSeconds = durationSeconds;
        Payload = payload;
        StreamFieldsJson = streamFieldsJson;
        Status = "Pending";
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public string StreamKey { get; private set; } = "replay_events";
    public Guid GroupId { get; private set; }
    public Guid MatchId { get; private set; }
    public MatchEventType Type { get; private set; }
    public DateTimeOffset EventTime { get; private set; }
    public int SecondsBeforeStart { get; private set; }
    public int DurationSeconds { get; private set; }
    public string Payload { get; private set; } = null!;
    public string StreamFieldsJson { get; private set; } = null!;
    public string Status { get; private set; } = "Pending";
    public string? Reason { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public string? RedisStreamEntryId { get; private set; }

    public void MarkPublished(string redisStreamEntryId)
    {
        Status = "Published";
        RedisStreamEntryId = redisStreamEntryId;
        ProcessedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = "Failed";
        Reason = reason;
    }
}
