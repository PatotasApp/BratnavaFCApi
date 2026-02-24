namespace BratnavaFC.Domain.Dtos;

public sealed record PlayerWeightDto(Guid PlayerId, double Weight);

public sealed record TeamOptionDto(
    List<PlayerWeightDto> TeamA,
    List<PlayerWeightDto> TeamB,
    List<PlayerWeightDto> Unassigned,
    double TeamAWeight,
    double TeamBWeight,
    double BalanceDiff,
    int GoalkeeperDiff,
    double SynergyTotal,
    double Score
);

public sealed record TeamsOptionsResultDto(List<TeamOptionDto> Options);