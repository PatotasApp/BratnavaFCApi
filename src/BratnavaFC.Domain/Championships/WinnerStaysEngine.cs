namespace BratnavaFC.Domain.Championships;

public sealed record WinnerStaysNextGame(Guid TeamAId, Guid TeamBId);

public sealed record WinnerStaysState(
    WinnerStaysNextGame NextGame,
    IReadOnlyList<Guid> WaitingQueue,
    Guid? ConsecutiveWinnerId,
    int ConsecutiveWins,
    int GamesProcessed);

public static class WinnerStaysEngine
{
    public static WinnerStaysState GetState(
        IEnumerable<Guid> orderedTeamIds,
        IEnumerable<ChampionshipMatch> history,
        WinnerStaysOptions options)
    {
        var teams = ChampionshipGuard.EnsureTeams(orderedTeamIds, minimumCount: 3);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(options);

        var knownTeams = teams.ToHashSet();
        var games = history.ToArray();
        var queue = new Queue<Guid>(teams.Skip(2));
        var field = new List<Guid> { teams[0], teams[1] };
        var enteredFieldAt = new Dictionary<Guid, int>
        {
            [teams[0]] = 0,
            [teams[1]] = 1
        };
        var nextEntryOrder = 2;
        Guid? consecutiveWinnerId = null;
        var consecutiveWins = 0;

        for (var gameIndex = 0; gameIndex < games.Length; gameIndex++)
        {
            var game = games[gameIndex];
            if (!knownTeams.Contains(game.TeamAId) || !knownTeams.Contains(game.TeamBId))
                throw new InvalidOperationException("O historico possui um time que nao participa do campeonato.");
            if (!field.Contains(game.TeamAId) || !field.Contains(game.TeamBId))
                throw new InvalidOperationException($"O jogo {gameIndex + 1} nao corresponde aos times que deveriam estar em campo.");

            if (game.IsDraw && options.TieRule != WinnerStaysTieRule.PenaltyShootout)
            {
                consecutiveWinnerId = null;
                consecutiveWins = 0;

                if (options.TieRule == WinnerStaysTieRule.BothLeave)
                {
                    BothLeave(field, queue, enteredFieldAt, ref nextEntryOrder);
                }
                else
                {
                    var longestOnField = field.MinBy(teamId => enteredFieldAt[teamId]);
                    ReplaceTeam(longestOnField, field, queue, enteredFieldAt, ref nextEntryOrder);
                }

                continue;
            }

            var winnerId = game.WinnerId;
            if (!winnerId.HasValue)
                throw new InvalidOperationException("Informe o vencedor dos penaltis para decidir quem permanece em campo.");

            var loserId = winnerId == game.TeamAId ? game.TeamBId : game.TeamAId;
            if (consecutiveWinnerId == winnerId)
                consecutiveWins++;
            else
            {
                consecutiveWinnerId = winnerId;
                consecutiveWins = 1;
            }

            var winnerReachedLimit = options.ConsecutiveWinsLimit > 0 &&
                                     consecutiveWins >= options.ConsecutiveWinsLimit;
            if (winnerReachedLimit)
            {
                // "Saem os dois" precisa de 2 times esperando; com 3 times sai só o vencedor.
                if (options.LimitRule == WinnerStaysLimitRule.BothLeave && teams.Length >= 4)
                    BothLeave(field, queue, enteredFieldAt, ref nextEntryOrder);
                else
                    ReplaceTeam(winnerId.Value, field, queue, enteredFieldAt, ref nextEntryOrder);

                consecutiveWinnerId = null;
                consecutiveWins = 0;
            }
            else
            {
                ReplaceTeam(loserId, field, queue, enteredFieldAt, ref nextEntryOrder);
            }
        }

        return new WinnerStaysState(
            new WinnerStaysNextGame(field[0], field[1]),
            Array.AsReadOnly(queue.ToArray()),
            consecutiveWinnerId,
            consecutiveWins,
            games.Length);
    }

    /// <summary>Os dois saem para o fim da fila, na ordem em que entraram em campo, e entram os próximos.</summary>
    private static void BothLeave(
        List<Guid> field,
        Queue<Guid> queue,
        Dictionary<Guid, int> enteredFieldAt,
        ref int nextEntryOrder)
    {
        var leavingTeams = field.OrderBy(teamId => enteredFieldAt[teamId]).ToArray();
        foreach (var leavingTeam in leavingTeams)
        {
            enteredFieldAt.Remove(leavingTeam);
            queue.Enqueue(leavingTeam);
        }

        field.Clear();
        AddNextTeam(field, queue, enteredFieldAt, ref nextEntryOrder);
        AddNextTeam(field, queue, enteredFieldAt, ref nextEntryOrder);
    }

    private static void ReplaceTeam(
        Guid leavingTeamId,
        List<Guid> field,
        Queue<Guid> queue,
        Dictionary<Guid, int> enteredFieldAt,
        ref int nextEntryOrder)
    {
        var position = field.IndexOf(leavingTeamId);
        enteredFieldAt.Remove(leavingTeamId);
        queue.Enqueue(leavingTeamId);
        var enteringTeamId = queue.Dequeue();
        field[position] = enteringTeamId;
        enteredFieldAt[enteringTeamId] = nextEntryOrder++;
    }

    private static void AddNextTeam(
        List<Guid> field,
        Queue<Guid> queue,
        Dictionary<Guid, int> enteredFieldAt,
        ref int nextEntryOrder)
    {
        var enteringTeamId = queue.Dequeue();
        field.Add(enteringTeamId);
        enteredFieldAt[enteringTeamId] = nextEntryOrder++;
    }
}
