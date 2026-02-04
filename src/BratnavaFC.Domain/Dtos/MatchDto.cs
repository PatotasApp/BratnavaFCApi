namespace BratnavaFC.Domain.Dtos;

public sealed record MatchDto(
    Guid Id,
    Guid GroupId,
    DateTime PlayedAt,
    int TeamAGoals,
    int TeamBGoals,
    string PlaceName,
    Guid? TeamAColorId,
    Guid? TeamBColorId
);
