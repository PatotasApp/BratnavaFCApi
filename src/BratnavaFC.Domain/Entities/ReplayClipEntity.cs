using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public sealed class ReplayClipEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid MatchId { get; private set; }
    public string BucketName { get; private set; } = default!;
    public string ObjectKey { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public string ETag { get; private set; } = default!;
    public DateTimeOffset RecordedAt { get; private set; }
    public MatchEventType EventType { get; private set; }

    // EF Core
    private ReplayClipEntity() { }

    public ReplayClipEntity(
        Guid groupId,
        Guid matchId,
        string bucketName,
        string objectKey,
        string contentType,
        string etag,
        DateTimeOffset recordedAt,
        MatchEventType eventType)
    {
        GroupId = groupId;
        MatchId = matchId;
        BucketName = bucketName;
        ObjectKey = objectKey;
        ContentType = contentType;
        ETag = etag;
        RecordedAt = recordedAt;
        EventType = eventType;
    }
}
