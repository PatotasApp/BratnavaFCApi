namespace BratnavaFC.Domain.Dtos.Notifications;

public sealed record NotificationItemDto(
    Guid      Id,
    string    Title,
    string    Body,
    string?   Type,
    string?   DataJson,
    bool      IsRead,
    DateTime  CreatedAt,
    Guid?     GroupId);

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationItemDto> Data,
    int Total,
    int Page,
    int PageSize);
