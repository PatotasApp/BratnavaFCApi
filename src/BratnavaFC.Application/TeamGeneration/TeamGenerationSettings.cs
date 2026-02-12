using System;

namespace BratnavaFC.Application.TeamGeneration;

public class TeamGenerationSettings
{
    public int PlayersPerTeam { get; init; } = 5;
    public bool IncludeGoalkeepers { get; init; } = true;

}
