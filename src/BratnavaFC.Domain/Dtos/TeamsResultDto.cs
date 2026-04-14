namespace BratnavaFC.Domain.Dtos;

public sealed record PlayerWeightDto(Guid PlayerId, double Weight)
{
    /// <summary>Normalized attack rating [0,1] for this player. Null when not rated.</summary>
    public double? AttackRatingNorm   { get; init; }

    /// <summary>Normalized defense rating [0,1] for this player. Null when not rated.</summary>
    public double? DefenseRatingNorm  { get; init; }

    /// <summary>Normalized physical rating [0,1] for this player. Null when not rated.</summary>
    public double? PhysicalRatingNorm { get; init; }
}

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
    public TeamOptionExplanationDto? Explanation  { get; init; }

    /// <summary>Difference in attack rating totals between teams. Null when no players are rated.</summary>
    public double? AttackDiff   { get; init; }

    /// <summary>Difference in defense rating totals between teams. Null when no players are rated.</summary>
    public double? DefenseDiff  { get; init; }

    /// <summary>Difference in physical rating totals between teams. Null when no players are rated.</summary>
    public double? PhysicalDiff { get; init; }
}

public sealed record TeamsOptionsResultDto(List<TeamOptionDto> Options);