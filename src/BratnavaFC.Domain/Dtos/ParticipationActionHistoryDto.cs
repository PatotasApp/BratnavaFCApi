namespace BratnavaFC.Domain.Dtos;

public sealed record ParticipationActionHistoryDto(
    Guid Id,
    string SubjectType,
    Guid SubjectId,
    Guid PlayerId,
    string PlayerName,
    string Action,
    string? Details,
    DateTime OccurredAt,
    bool PerformedByAdmin);
