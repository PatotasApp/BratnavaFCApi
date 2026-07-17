namespace BratnavaFC.Domain.Dtos;

public record PublicClipDto(
    Guid Id,
    string VideoUrl,
    string EventType,
    DateTimeOffset RecordedAt,
    int? GoalNumber,
    int? TotalGoals,
    string? TeamAColorName = null,
    string? TeamAColorHex = null,
    string? TeamBColorName = null,
    string? TeamBColorHex = null
);
