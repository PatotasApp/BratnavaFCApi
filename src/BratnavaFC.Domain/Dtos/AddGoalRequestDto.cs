namespace BratnavaFC.Domain.Dtos;

public sealed record AddGoalRequestDto(
    Guid ScorerPlayerId,
    Guid? AssistPlayerId,
    string? Time,
    bool IsOwnGoal = false
);
