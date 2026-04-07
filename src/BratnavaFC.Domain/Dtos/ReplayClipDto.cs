namespace BratnavaFC.Domain.Dtos;

public record ReplayClipDto(
    Guid Id,
    string ObjectKey,
    string VideoUrl,
    string EventType,
    DateTimeOffset UploadedAt,
    int LikeCount,
    bool IsLikedByMe,
    bool IsFavoritedByMe
);
