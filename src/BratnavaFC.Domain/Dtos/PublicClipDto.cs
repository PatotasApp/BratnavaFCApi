namespace BratnavaFC.Domain.Dtos;

public record PublicClipDto(
    Guid Id,
    string VideoUrl,
    string EventType,
    DateTimeOffset RecordedAt,
    int? GoalNumber,
    int? TotalGoals
);
