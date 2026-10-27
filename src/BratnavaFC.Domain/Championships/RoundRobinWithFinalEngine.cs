namespace BratnavaFC.Domain.Championships;

/// <summary>Jogo eliminatório (hoje só a final do "todos contra todos + final").</summary>
public sealed record KnockoutGame(Guid TeamAId, Guid TeamBId);

public static class RoundRobinWithFinalEngine
{
    public static IReadOnlyList<ChampionshipRound> GenerateQualifying(
        IEnumerable<Guid> teamIds,
        int turns = 1) =>
        RoundRobinScheduleGenerator.Generate(teamIds, turns);

    public static KnockoutGame CreateFinal(IEnumerable<Guid> orderedStandingTeamIds)
    {
        var teams = ChampionshipGuard.EnsureTeams(orderedStandingTeamIds);
        return new KnockoutGame(teams[0], teams[1]);
    }
}
