using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Statistics;

/// <summary>
/// Uma unidade de estatística vinda de um campeonato, com o que o histórico precisa mostrar.
/// AnchorMatchId: partida real à qual a linha de contribuição se prende (FK para Matches):
/// o próprio jogo, ou, no consolidado, o último jogo do campeonato.
/// </summary>
public sealed record ChampionshipUnit(StatMatch Stat, ChampionshipEntity Championship, MatchEntity? Game, Guid AnchorMatchId);

/// <summary>
/// Fase 3: transforma cada campeonato FINALIZADO em unidades de estatística conforme as regras dele.
/// É o único lugar (além do ChampionshipService) que enxerga os jogos de campeonato,
/// e está na allowlist do teste de arquitetura.
///
/// Regras de contagem (docs/campeonato/DECISOES.md, seção "Fase 3"):
/// - Consolidado: 1 unidade por campeonato; cada time é um lado; resultado pela regra.
/// - Jogos separados: 1 unidade por jogo; resultado pelo placar (PerGame) ou pela regra.
/// - Só no campeonato: nenhuma unidade.
/// - Gols, assistências, gols contra e feitos (hat-trick, clean sheet) só contam com
///   GoalsCountInGeneralStats; MVPs e votos só com MvpCountsInGeneralStats.
/// - Quem não jogou nenhum jogo (ou DidNotPlay) não entra.
/// </summary>
public sealed class ChampionshipStatsSource
{
    private readonly AppDbContext _db;

    public ChampionshipStatsSource(AppDbContext db) => _db = db;

    public async Task<List<ChampionshipUnit>> GetUnitsAsync(Guid groupId, CancellationToken ct)
    {
        var championships = await _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Status == ChampionshipStatus.Finalized)
            .Include(c => c.Teams).ThenInclude(t => t.Color)
            .Include(c => c.Players).ThenInclude(p => p.Player)
            .Include(c => c.Votes)
            .AsSplitQuery()
            .ToListAsync(ct);

        championships = championships.Where(c => c.Rules.HistoryMode != HistoryMode.ChampionshipOnly).ToList();
        if (championships.Count == 0) return [];

        var ids = championships.Select(c => c.Id).ToList();
        var games = await FinishedGamesQuery()
            .Where(m => ids.Contains(m.ChampionshipId!.Value))
            .ToListAsync(ct);
        var gamesByChampionship = games.ToLookup(g => g.ChampionshipId!.Value);

        var units = new List<ChampionshipUnit>();
        foreach (var championship in championships)
        {
            var champGames = gamesByChampionship[championship.Id].OrderBy(g => g.ChampionshipGameOrder).ToList();
            if (champGames.Count == 0) continue;

            if (championship.Rules.HistoryMode == HistoryMode.Consolidated)
                units.Add(new ChampionshipUnit(Consolidated(championship, champGames), championship, null, champGames[^1].Id));
            else
                units.AddRange(SeparateGames(championship, champGames)
                    .Select(x => new ChampionshipUnit(x.Stat, championship, x.Game, x.Game.Id)));
        }

        return units;
    }

    /// <summary>Jogos que entram no sorteio de times: só de campeonatos com IncludeInTeamDraw.</summary>
    public async Task<List<MatchEntity>> GetTeamDrawGamesAsync(HashSet<Guid> playerIds, CancellationToken ct)
    {
        var championshipIds = await _db.Championships.AsNoTracking()
            .Where(c => c.Status == ChampionshipStatus.Finalized)
            .Select(c => new { c.Id, c.RulesJson })
            .ToListAsync(ct);

        var included = championshipIds
            .Where(c => RulesOf(c.RulesJson).IncludeInTeamDraw)
            .Select(c => c.Id)
            .ToList();
        if (included.Count == 0) return [];

        return await FinishedGamesQuery()
            .Where(m => included.Contains(m.ChampionshipId!.Value)
                        && m.Players.Any(mp => playerIds.Contains(mp.PlayerId)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Jogos que contam exatamente como partidas normais: jogos separados, V/E/D pelo placar,
    /// gols e MVP nas estatísticas gerais. Só esses entram em análises de formação (team builder).
    /// </summary>
    public async Task<List<Guid>> GetFullyCountingGameIdsAsync(Guid groupId, CancellationToken ct)
    {
        var championships = await _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Status == ChampionshipStatus.Finalized)
            .Select(c => new { c.Id, c.RulesJson })
            .ToListAsync(ct);

        var ids = championships
            .Where(c => RulesOf(c.RulesJson) is
            {
                HistoryMode: HistoryMode.SeparateGames, ResultMode: ResultMode.PerGame,
                GoalsCountInGeneralStats: true, MvpCountsInGeneralStats: true
            })
            .Select(c => c.Id)
            .ToList();
        if (ids.Count == 0) return [];

        return await _db.Matches.IgnoreQueryFilters()
            .Where(m => m.ChampionshipId != null && ids.Contains(m.ChampionshipId.Value) && m.Status == MatchStatus.Finalized)
            .Select(m => m.Id)
            .ToListAsync(ct);
    }

    private IQueryable<MatchEntity> FinishedGamesQuery() => _db.Matches.IgnoreQueryFilters().AsNoTracking()
        .Where(m => m.ChampionshipId != null && m.Status == MatchStatus.Finalized)
        .Include(m => m.Players)
        .Include(m => m.Goals)
        .AsSplitQuery();

    private static ChampionshipRuleSet RulesOf(string rulesJson) =>
        System.Text.Json.JsonSerializer.Deserialize<ChampionshipRuleSet>(rulesJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    // ── Consolidado ──────────────────────────────────────────────────────────

    private static StatMatch Consolidated(ChampionshipEntity c, IReadOnlyList<MatchEntity> games)
    {
        var rules = c.Rules;
        var played = PlayedGamesByPlayer(games);
        var participants = c.Players.Where(p => !p.DidNotPlay && played.ContainsKey(p.PlayerId)).ToList();

        // Time de cada jogador: o do último jogo dele (vale também para quem trocou de time).
        var teamOf = participants.ToDictionary(p => p.PlayerId, p => LastTeam(games, p.PlayerId) ?? p.TeamId);
        var gameMvps = games.SelectMany(g => g.Players).Where(mp => mp.IsMvp == true).Select(mp => mp.PlayerId).ToHashSet();

        var sides = new List<StatSide>();
        foreach (var team in c.Teams.OrderBy(t => t.Order))
        {
            var teamResult = TeamResult(rules, team.FinalPosition ?? c.Teams.Count);
            var players = participants
                .Where(p => teamOf[p.PlayerId] == team.Id)
                .Select(p => new StatPlayer(
                    p.PlayerId,
                    p.IsGoalkeeper,
                    IsMvp(rules, p.IsMvp, gameMvps.Contains(p.PlayerId)),
                    PlayerResult(rules, teamResult, played[p.PlayerId]),
                    rules.GoalsCountInGeneralStats ? FeatsAcrossGames(games, p.PlayerId) : StatFeats.Zero))
                .ToList();
            if (players.Count > 0)
                sides.Add(new StatSide(players, teamResult, null, team.Id));
        }

        var playedIds = participants.Select(p => p.PlayerId).ToHashSet();
        var goals = rules.GoalsCountInGeneralStats ? games.SelectMany(GoalsOf).ToList() : [];
        var votes = rules.MvpCountsInGeneralStats
            ? c.Votes.Where(v => rules.MvpMode == MvpMode.PerGame || v.GameId is null)
                .Where(v => playedIds.Contains(v.VotedPlayerId))
                .Select(v => v.VotedPlayerId).ToList()
            : [];

        return new StatMatch(c.Id, c.GroupId, c.PlayedAt, sides, goals, votes,
            HasScore: rules.ResultMode != ResultMode.PresenceOnly, IsHeadToHead: false, c.Id);
    }

    // ── Jogos separados ──────────────────────────────────────────────────────

    private static IEnumerable<(StatMatch Stat, MatchEntity Game)> SeparateGames(ChampionshipEntity c, IReadOnlyList<MatchEntity> games)
    {
        var rules = c.Rules;
        var played = PlayedGamesByPlayer(games);
        var excluded = c.Players.Where(p => p.DidNotPlay).Select(p => p.PlayerId).ToHashSet();
        var positionOf = c.Teams.ToDictionary(t => t.Id, t => t.FinalPosition ?? c.Teams.Count);

        // MVP do campeonato (e seus votos) ficam no último jogo de cada jogador.
        var lastGameOf = played.Keys.ToDictionary(id => id, id => games.Last(g => PlayedIn(g, id)).Id);
        var championshipMvps = c.Players.Where(p => p.IsMvp).Select(p => p.PlayerId).ToHashSet();

        foreach (var game in games)
        {
            var baseStat = StatMatch.FromMatch(game);
            var teamIds = new[] { game.ChampionshipTeamAId!.Value, game.ChampionshipTeamBId!.Value };

            var sides = baseStat.Sides.Select((side, index) =>
            {
                var sideResult = rules.ResultMode == ResultMode.PerGame
                    ? side.Result
                    : TeamResult(rules, positionOf.GetValueOrDefault(teamIds[index], c.Teams.Count));

                var players = side.Players
                    .Where(p => !excluded.Contains(p.PlayerId))
                    .Select(p => p with
                    {
                        Result = rules.ResultMode == ResultMode.PerGame ? side.Result : PlayerResult(rules, sideResult, played[p.PlayerId]),
                        IsMvp = rules.MvpCountsInGeneralStats && rules.MvpMode switch
                        {
                            MvpMode.PerGame => p.IsMvp,
                            MvpMode.PerChampionship => championshipMvps.Contains(p.PlayerId) && lastGameOf[p.PlayerId] == game.Id,
                            _ => false
                        },
                        Feats = rules.GoalsCountInGeneralStats ? null : StatFeats.Zero
                    })
                    .ToList();

                return new StatSide(players, sideResult, side.GoalsAgainst, teamIds[index]);
            }).ToList();

            var votes = !rules.MvpCountsInGeneralStats ? [] : rules.MvpMode switch
            {
                MvpMode.PerGame => c.Votes.Where(v => v.GameId == game.Id).Select(v => v.VotedPlayerId).ToList(),
                MvpMode.PerChampionship => c.Votes
                    .Where(v => v.GameId is null && lastGameOf.TryGetValue(v.VotedPlayerId, out var last) && last == game.Id)
                    .Select(v => v.VotedPlayerId).ToList(),
                _ => new List<Guid>()
            };

            var stat = baseStat with
            {
                Sides = sides,
                Goals = rules.GoalsCountInGeneralStats ? baseStat.Goals : [],
                MvpVotesFor = votes,
                HasScore = rules.ResultMode != ResultMode.PresenceOnly,
                IsHeadToHead = rules.ResultMode == ResultMode.PerGame,
                ChampionshipId = c.Id
            };
            yield return (stat, game);
        }
    }

    // ── Regras ───────────────────────────────────────────────────────────────

    private static StatResult TeamResult(ChampionshipRuleSet rules, int position) => rules.ResultMode switch
    {
        ResultMode.WinForAll => StatResult.Win,
        ResultMode.Placement => rules.ResultForPosition(position) switch
        {
            PlacementResult.Win => StatResult.Win,
            PlacementResult.Draw => StatResult.Draw,
            PlacementResult.Loss => StatResult.Loss,
            _ => StatResult.None
        },
        _ => StatResult.None
    };

    /// <summary>Colocação só vale para quem jogou o mínimo de jogos; abaixo disso, só presença.</summary>
    private static StatResult PlayerResult(ChampionshipRuleSet rules, StatResult teamResult, int gamesPlayed) =>
        rules.ResultMode == ResultMode.Placement && gamesPlayed < rules.MinGamesForPlacementResult
            ? StatResult.None
            : teamResult;

    private static bool IsMvp(ChampionshipRuleSet rules, bool championshipMvp, bool anyGameMvp) =>
        rules.MvpCountsInGeneralStats && rules.MvpMode switch
        {
            MvpMode.PerChampionship => championshipMvp,
            MvpMode.PerGame => anyGameMvp, // conta como 1 partida: no máximo 1 MVP
            _ => false
        };

    /// <summary>Feitos por jogo, nunca somados entre jogos; no consolidado, cada feito conta no máximo 1 vez.</summary>
    private static StatFeats FeatsAcrossGames(IReadOnlyList<MatchEntity> games, Guid playerId)
    {
        int hat = 0, poker = 0, five = 0, goalAndAssist = 0, threeAssists = 0, clean = 0;
        foreach (var game in games.Where(g => PlayedIn(g, playerId)))
        {
            var goals = GoalsOf(game).ToList();
            var scored = goals.Count(g => !g.IsOwnGoal && g.ScorerPlayerId == playerId);
            var assists = goals.Count(g => !g.IsOwnGoal && g.AssistPlayerId == playerId);
            hat |= scored == 3 ? 1 : 0;
            poker |= scored == 4 ? 1 : 0;
            five |= scored >= 5 ? 1 : 0;
            goalAndAssist |= scored > 0 && assists > 0 ? 1 : 0;
            threeAssists |= assists >= 3 ? 1 : 0;

            var mp = game.Players.First(p => p.PlayerId == playerId && !p.DidNotPlay);
            var conceded = mp.Team == 1 ? game.TeamBGoals : game.TeamAGoals;
            clean |= mp.IsGoalkeeper && conceded == 0 ? 1 : 0;
        }
        return new StatFeats(hat, poker, five, goalAndAssist, threeAssists, clean);
    }

    private static IEnumerable<StatGoal> GoalsOf(MatchEntity game) => StatMatch.FromMatch(game).Goals;

    private static bool PlayedIn(MatchEntity game, Guid playerId) =>
        game.Players.Any(mp => mp.PlayerId == playerId && !mp.DidNotPlay && mp.Team is 1 or 2);

    private static Dictionary<Guid, int> PlayedGamesByPlayer(IEnumerable<MatchEntity> games) => games
        .SelectMany(g => g.Players.Where(mp => !mp.DidNotPlay && mp.Team is 1 or 2).Select(mp => mp.PlayerId).Distinct())
        .GroupBy(id => id)
        .ToDictionary(g => g.Key, g => g.Count());

    private static Guid? LastTeam(IReadOnlyList<MatchEntity> games, Guid playerId)
    {
        var game = games.LastOrDefault(g => PlayedIn(g, playerId));
        if (game is null) return null;
        var team = game.Players.First(mp => mp.PlayerId == playerId).Team;
        return team == 1 ? game.ChampionshipTeamAId : game.ChampionshipTeamBId;
    }
}
