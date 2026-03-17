namespace BratnavaFC.Application.TeamGeneration;

public sealed record TeamGenerationSettings
{
    public int  PlayersPerTeam     { get; init; } = 5;
    public bool IncludeGoalkeepers { get; init; } = true;
}
