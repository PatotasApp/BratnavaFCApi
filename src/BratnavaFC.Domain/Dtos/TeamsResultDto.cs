namespace BratnavaFC.Domain.Dtos;

public sealed record PlayerWeightDto(Guid PlayerId, double Weight);

public sealed class TeamOptionExplanationDto
{
    public string Resumo       { get; init; } = "";
    public string AnaliseTimeA { get; init; } = "";
    public string AnaliseTimeB { get; init; } = "";
    public string Conclusao    { get; init; } = "";
}

public sealed record TeamOptionDto(
    List<PlayerWeightDto> TeamA,
    List<PlayerWeightDto> TeamB,
    List<PlayerWeightDto> Unassigned,
    double TeamAWeight,
    double TeamBWeight,
    double BalanceDiff,
    double SynergyTotal,
    double Score
)
{
    public TeamOptionExplanationDto? Explanation { get; init; }
}

public sealed record TeamsOptionsResultDto(List<TeamOptionDto> Options);