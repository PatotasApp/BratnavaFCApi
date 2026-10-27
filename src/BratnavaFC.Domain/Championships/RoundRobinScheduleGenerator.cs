namespace BratnavaFC.Domain.Championships;

public sealed record ScheduledChampionshipGame(
    int RoundNumber,
    int Leg,
    Guid TeamAId,
    Guid TeamBId);

public sealed record ChampionshipRound(
    int RoundNumber,
    int Leg,
    IReadOnlyList<ScheduledChampionshipGame> Games,
    Guid? ByeTeamId);

public static class RoundRobinScheduleGenerator
{
    public static IReadOnlyList<ChampionshipRound> Generate(IEnumerable<Guid> teamIds, int turns = 1)
    {
        var teams = ChampionshipGuard.EnsureTeams(teamIds);
        if (turns is not (1 or 2))
            throw new InvalidOperationException("O numero de turnos deve ser 1 ou 2.");

        var rotation = teams.Select(id => (Guid?)id).ToList();
        if (rotation.Count % 2 != 0)
            rotation.Add(null);

        var roundsPerTurn = rotation.Count - 1;
        var firstTurn = new List<ChampionshipRound>(roundsPerTurn);

        for (var roundIndex = 0; roundIndex < roundsPerTurn; roundIndex++)
        {
            var games = new List<ScheduledChampionshipGame>(rotation.Count / 2);
            Guid? byeTeamId = null;

            for (var index = 0; index < rotation.Count / 2; index++)
            {
                var first = rotation[index];
                var second = rotation[rotation.Count - 1 - index];
                if (!first.HasValue || !second.HasValue)
                {
                    byeTeamId = first ?? second;
                    continue;
                }

                // Alterna o mando apenas para não privilegiar o primeiro time fixo.
                var invert = index == 0 && roundIndex % 2 != 0;
                games.Add(new ScheduledChampionshipGame(
                    roundIndex + 1,
                    1,
                    invert ? second.Value : first.Value,
                    invert ? first.Value : second.Value));
            }

            firstTurn.Add(new ChampionshipRound(
                roundIndex + 1,
                1,
                Array.AsReadOnly(games.ToArray()),
                byeTeamId));

            Rotate(rotation);
        }

        if (turns == 1)
            return firstTurn.AsReadOnly();

        var result = new List<ChampionshipRound>(firstTurn.Count * 2);
        result.AddRange(firstTurn);
        foreach (var round in firstTurn)
        {
            var roundNumber = round.RoundNumber + roundsPerTurn;
            var returnGames = round.Games
                .Select(game => new ScheduledChampionshipGame(
                    roundNumber,
                    2,
                    game.TeamBId,
                    game.TeamAId))
                .ToArray();
            result.Add(new ChampionshipRound(
                roundNumber,
                2,
                Array.AsReadOnly(returnGames),
                round.ByeTeamId));
        }

        return result.AsReadOnly();
    }

    private static void Rotate(List<Guid?> teams)
    {
        var last = teams[^1];
        teams.RemoveAt(teams.Count - 1);
        teams.Insert(1, last);
    }
}
