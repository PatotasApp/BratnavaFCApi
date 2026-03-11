namespace BratnavaFC.Domain.Dtos;

public sealed record UpdateGoalRequestDto(
    Guid ScorerPlayerId,
    Guid? AssistPlayerId,
    string? Time,
    bool IsOwnGoal = false
);
