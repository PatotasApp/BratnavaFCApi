namespace BratnavaFC.Domain.Championships;

public sealed record KnockoutGame(Guid TeamAId, Guid TeamBId);

public sealed record KnockoutRound(
    int RoundNumber,
    IReadOnlyList<KnockoutGame> Games,
    IReadOnlyList<Guid> AutomaticallyAdvancedTeamIds)
{
    public bool IsFinal => Games.Count == 1 && AutomaticallyAdvancedTeamIds.Count == 0;
}

public static class KnockoutEngine
{
    public static KnockoutRound CreateRound(IEnumerable<Guid> orderedTeamIds, int roundNumber = 1)
    {
        var teams = ChampionshipGuard.EnsureTeams(orderedTeamIds);
        if (roundNumber <= 0)
            throw new InvalidOperationException("O numero da fase deve ser maior que zero.");

        var games = new List<KnockoutGame>(teams.Length / 2);
        for (var index = 0; index + 1 < teams.Length; index += 2)
            games.Add(new KnockoutGame(teams[index], teams[index + 1]));

        var automaticallyAdvanced = teams.Length % 2 == 0
            ? Array.Empty<Guid>()
            : new[] { teams[^1] };

        return new KnockoutRound(
            roundNumber,
            Array.AsReadOnly(games.ToArray()),
            Array.AsReadOnly(automaticallyAdvanced));
    }

    public static KnockoutRound CreateNextRound(
        KnockoutRound currentRound,
        IEnumerable<ChampionshipMatch> results)
    {
        ArgumentNullException.ThrowIfNull(currentRound);
        ArgumentNullException.ThrowIfNull(results);
        var completedGames = results.ToArray();
        if (completedGames.Length != currentRound.Games.Count)
            throw new InvalidOperationException("Informe o resultado de todos os jogos da fase eliminatoria.");

        var advancingTeams = new List<Guid>(
            currentRound.Games.Count + currentRound.AutomaticallyAdvancedTeamIds.Count);
        for (var index = 0; index < currentRound.Games.Count; index++)
        {
            var scheduled = currentRound.Games[index];
            var result = completedGames[index];
            var sameMatch = (scheduled.TeamAId == result.TeamAId && scheduled.TeamBId == result.TeamBId) ||
                            (scheduled.TeamAId == result.TeamBId && scheduled.TeamBId == result.TeamAId);
            if (!sameMatch)
                throw new InvalidOperationException($"O resultado {index + 1} nao corresponde ao jogo da fase eliminatoria.");
            if (!result.WinnerId.HasValue)
                throw new InvalidOperationException("Jogo eliminatorio empatado exige um vencedor externo.");

            advancingTeams.Add(result.WinnerId.Value);
        }

        advancingTeams.AddRange(currentRound.AutomaticallyAdvancedTeamIds);
        if (advancingTeams.Count < 2)
            throw new InvalidOperationException("A fase eliminatoria ja possui um campeao.");

        return CreateRound(advancingTeams, currentRound.RoundNumber + 1);
    }

    public static Guid GetChampion(KnockoutRound finalRound, ChampionshipMatch finalResult)
    {
        ArgumentNullException.ThrowIfNull(finalRound);
        ArgumentNullException.ThrowIfNull(finalResult);
        if (!finalRound.IsFinal)
            throw new InvalidOperationException("A fase informada nao e uma final.");

        var final = finalRound.Games[0];
        var sameMatch = (final.TeamAId == finalResult.TeamAId && final.TeamBId == finalResult.TeamBId) ||
                        (final.TeamAId == finalResult.TeamBId && final.TeamBId == finalResult.TeamAId);
        if (!sameMatch)
            throw new InvalidOperationException("O resultado informado nao corresponde a final.");

        return finalResult.WinnerId
            ?? throw new InvalidOperationException("Final empatada exige um vencedor externo.");
    }
}

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
