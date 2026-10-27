namespace BratnavaFC.Domain.Championships;

public static class GameEndEvaluator
{
    public static bool ShouldEnd(
        GameEndOptions options,
        TimeSpan elapsed,
        int teamAGoals,
        int teamBGoals)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (elapsed < TimeSpan.Zero)
            throw new InvalidOperationException("O tempo decorrido nao pode ser negativo.");
        if (teamAGoals < 0 || teamBGoals < 0)
            throw new InvalidOperationException("Placar nao pode ser negativo.");

        var reachedTimeLimit = options.TimeLimit.HasValue && elapsed >= options.TimeLimit.Value;
        var reachedGoalLimit = options.GoalLimit.HasValue &&
                               Math.Max(teamAGoals, teamBGoals) >= options.GoalLimit.Value;
        return reachedTimeLimit || reachedGoalLimit;
    }
}

public static class ChampionshipEndEvaluator
{
    public static bool ShouldEnd(
        ChampionshipEndOptions options,
        IEnumerable<Guid> teamIds,
        IEnumerable<ChampionshipMatch> matches,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(options);
        var teams = ChampionshipGuard.EnsureTeams(teamIds);
        ArgumentNullException.ThrowIfNull(matches);
        var games = matches.ToArray();
        if (elapsed < TimeSpan.Zero)
            throw new InvalidOperationException("O tempo decorrido nao pode ser negativo.");

        var teamSet = teams.ToHashSet();
        if (games.Any(game => !teamSet.Contains(game.TeamAId) || !teamSet.Contains(game.TeamBId)))
            throw new InvalidOperationException("O historico possui um time que nao participa do campeonato.");

        return options.Mode switch
        {
            ChampionshipEndMode.TotalTime => elapsed >= options.TotalTime!.Value,
            ChampionshipEndMode.NumberOfGames => games.Length >= options.NumberOfGames!.Value,
            ChampionshipEndMode.EqualGamesForAllTeams => AllTeamsHaveEqualGames(teams, games),
            _ => throw new InvalidOperationException("Regra de fim do campeonato invalida.")
        };
    }

    private static bool AllTeamsHaveEqualGames(
        IReadOnlyList<Guid> teamIds,
        IReadOnlyList<ChampionshipMatch> matches)
    {
        if (matches.Count == 0)
            return false;

        var gamesByTeam = teamIds.ToDictionary(teamId => teamId, _ => 0);
        foreach (var game in matches)
        {
            gamesByTeam[game.TeamAId]++;
            gamesByTeam[game.TeamBId]++;
        }

        var firstCount = gamesByTeam[teamIds[0]];
        return firstCount > 0 && gamesByTeam.Values.All(count => count == firstCount);
    }
}
