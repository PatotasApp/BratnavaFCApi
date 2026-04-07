namespace BratnavaFC.Domain.Dtos;

public record LikedReplayClipDto(
    Guid Id,
    Guid MatchId,
    string ObjectKey,
    string VideoUrl,
    string EventType,
    DateTimeOffset RecordedAt,
    int LikeCount,
    bool IsLikedByMe,
    bool IsFavoritedByMe
);
