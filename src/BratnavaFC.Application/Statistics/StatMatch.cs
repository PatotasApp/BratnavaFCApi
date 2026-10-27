using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Statistics;

/// <summary>Resultado de um jogador numa unidade de estatística. None = só presença.</summary>
public enum StatResult
{
    None = 0,
    Win = 1,
    Draw = 2,
    Loss = 3
}

/// <summary>
/// Feitos já apurados (hat-trick, clean sheet...). Usado quando a unidade junta vários jogos
/// (campeonato consolidado): os feitos são por jogo e não podem sair da soma dos gols.
/// </summary>
public sealed record StatFeats(
    int HatTricks, int Pokers, int FiveGoalGames, int GoalAndAssistGames, int ThreeAssistGames, int CleanSheets)
{
    public static StatFeats Zero { get; } = new(0, 0, 0, 0, 0, 0);
}

public sealed record StatPlayer(Guid PlayerId, bool IsGoalkeeper, bool IsMvp, StatResult Result, StatFeats? Feats = null);

/// <summary>Um lado da unidade: um time de uma partida, ou um time do campeonato consolidado.</summary>
/// <summary>TeamId: time do campeonato desse lado (nulo em partida comum).</summary>
public sealed record StatSide(IReadOnlyList<StatPlayer> Players, StatResult Result, int? GoalsAgainst, Guid? TeamId = null);

/// <summary>Autor nulo = jogador que não apareceu (DidNotPlay): o gol não conta para ele, mas a assistência conta.</summary>
public sealed record StatGoal(Guid? ScorerPlayerId, Guid? AssistPlayerId, bool IsOwnGoal);

/// <summary>
/// Fonte única das estatísticas: uma partida comum, um jogo de campeonato que conta como
/// partida, ou um campeonato consolidado. Os leitores (relatório visual, spotlight,
/// conquistas, histórico) calculam tudo a partir disto, sem saber de onde veio.
/// </summary>
public sealed record StatMatch(
    Guid Id,
    Guid GroupId,
    DateTime PlayedAt,
    IReadOnlyList<StatSide> Sides,
    IReadOnlyList<StatGoal> Goals,
    IReadOnlyList<Guid> MvpVotesFor,
    bool HasScore,
    bool IsHeadToHead,
    Guid? ChampionshipId)
{
    public IEnumerable<StatPlayer> AllPlayers => Sides.SelectMany(s => s.Players);

    /// <summary>
    /// Partida comum → unidade. Mesmas regras que os leitores sempre usaram: quem não
    /// apareceu (DidNotPlay) fica fora, e gols/assistências/votos dele são ignorados.
    /// </summary>
    public static StatMatch FromMatch(MatchEntity match)
    {
        var hasScore = match.TeamAGoals.HasValue && match.TeamBGoals.HasValue;
        var a = match.TeamAGoals ?? 0;
        var b = match.TeamBGoals ?? 0;

        StatSide Side(short team)
        {
            var result = !hasScore ? StatResult.None
                : a == b ? StatResult.Draw
                : (team == 1) == (a > b) ? StatResult.Win : StatResult.Loss;

            var players = match.Players
                .Where(mp => mp.Team == team && !mp.DidNotPlay)
                .GroupBy(mp => mp.PlayerId)
                .Select(g => g.First())
                .Select(mp => new StatPlayer(mp.PlayerId, mp.IsGoalkeeper, mp.IsMvp == true, result))
                .ToList();

            return new StatSide(players, result, hasScore ? (team == 1 ? b : a) : null);
        }

        var played = match.Players.Where(mp => !mp.DidNotPlay).ToDictionary(mp => mp.Id, mp => mp.PlayerId);

        var goals = new List<StatGoal>();
        foreach (var goal in match.Goals ?? [])
        {
            Guid? scorer = played.TryGetValue(goal.ScorerMatchPlayerId, out var s1) ? s1 : null;
            Guid? assist = goal.AssistMatchPlayerId.HasValue && played.TryGetValue(goal.AssistMatchPlayerId.Value, out var a2)
                ? a2
                : null;
            goals.Add(new StatGoal(scorer, assist, goal.IsOwnGoal));
        }

        var votes = (match.Votes ?? [])
            .Where(v => played.ContainsKey(v.VotedForId))
            .Select(v => played[v.VotedForId])
            .ToList();

        return new StatMatch(match.Id, match.GroupId, match.PlayedAt, [Side(1), Side(2)], goals, votes,
            hasScore, IsHeadToHead: true, match.ChampionshipId);
    }
}
