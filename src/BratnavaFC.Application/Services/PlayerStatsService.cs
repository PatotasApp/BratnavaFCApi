using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class PlayerStatsService : IPlayerStatsService
{
    private readonly AppDbContext _context;

    public PlayerStatsService(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerEntity> players, CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (players.Count == 0) return new List<PlayerStats>();

        var playerIds = players.Select(p => p.Id).ToHashSet();
        var matches = await LoadMatchesAsync(playerIds, cancellationToken);

        var perPlayer = InitializePlayerAccumulators(playerIds);
        var pairTotals = new Dictionary<PairKey, PairAccumulator>();

        foreach (var match in matches)
        {
            ProcessMatchForBasicStats(match, playerIds, perPlayer, pairTotals);
        }

        var result = new List<PlayerStats>(players.Count);

        foreach (var pl in players)
        {
            var acc = perPlayer.TryGetValue(pl.Id, out var a) ? a : PlayerAccumulator.Empty;

            var winRate = acc.MatchesPlayed == 0
                ? 0.0
                : acc.Wins / (double)acc.MatchesPlayed;

            var synergy = BuildSynergyMap(pl.Id, players, pairTotals);

            result.Add(new PlayerStats
            {
                PlayerId = pl.Id,
                Wins = acc.Wins,
                Ties = acc.Ties,
                Losses = acc.Losses,
                WinRate = winRate,
                SynergyWith = synergy
            });
        }

        return result;
    }

    // ✅ MODO VISUAL
    public async Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        // 1) Players do grupo
        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .ToListAsync(cancellationToken);

        if (players.Count == 0)
        {
            return new PlayerVisualStatsReport
            {
                GroupId = groupId,
                TotalMatchesConsidered = 0,
                TotalFinalizedMatches = 0,
                TotalMatchesWithScore = 0,
                Players = new List<PlayerVisualStatsItem>()
            };
        }

        var playerIds = players.Select(p => p.Id).ToHashSet();

        // 2) Matches com MatchPlayers desse grupo (inclui Players e MVP flag)
        var matches = await _context.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .Where(m => m.Players.Any(mp => playerIds.Contains(mp.PlayerId)))
            .ToListAsync(cancellationToken);

        var totalMatches = matches.Count;
        var totalFinalized = matches.Count(m => m.IsFinalized);
        var totalWithScore = matches.Count(m => m.TeamAGoals.HasValue && m.TeamBGoals.HasValue);

        // 3) Acumuladores (agora também MVP)
        var perPlayer = InitializePlayerAccumulators(playerIds);
        var mvpCounts = playerIds.ToDictionary(id => id, _ => 0);

        var pairTotals = new Dictionary<PairKey, PairAccumulator>();

        foreach (var match in matches)
        {
            ProcessMatchForVisual(match, playerIds, perPlayer, pairTotals, mvpCounts);
        }

        // 4) Mapa nome por id (pra synergies “visuais”)
        var playerNameById = players.ToDictionary(p => p.Id, p => p.Name);

        // 5) Monta o report
        var items = new List<PlayerVisualStatsItem>(players.Count);

        foreach (var pl in players)
        {
            var acc = perPlayer.TryGetValue(pl.Id, out var a) ? a : PlayerAccumulator.Empty;

            var winRate = acc.MatchesPlayed == 0
                ? 0.0
                : acc.Wins / (double)acc.MatchesPlayed;

            var synergies = BuildSynergyVisual(pl.Id, playerIds, playerNameById, pairTotals);

            items.Add(new PlayerVisualStatsItem
            {
                PlayerId = pl.Id,
                Name = pl.Name,
                Status = pl.Status,
                IsGoalkeeper = pl.IsGoalkeeper,

                GamesPlayed = acc.MatchesPlayed,
                Wins = acc.Wins,
                Ties = acc.Ties,
                Losses = acc.Losses,
                WinRate = winRate,

                Mvps = mvpCounts.TryGetValue(pl.Id, out var mvps) ? mvps : 0,

                // deixa synergies mais úteis: ordena por matches juntos desc, depois winrate desc
                Synergies = synergies
                    .OrderByDescending(s => s.MatchesTogether)
                    .ThenByDescending(s => s.WinRateTogether)
                    .ToList()
            });
        }

        // ordena players por winrate e jogos
        items = items
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.GamesPlayed)
            .ThenByDescending(p => p.Mvps)
            .ThenBy(p => p.Name)
            .ToList();

        return new PlayerVisualStatsReport
        {
            GroupId = groupId,
            TotalMatchesConsidered = totalMatches,
            TotalFinalizedMatches = totalFinalized,
            TotalMatchesWithScore = totalWithScore,
            Players = items
        };
    }

    private Task<List<MatchEntity>> LoadMatchesAsync(HashSet<Guid> playerIds, CancellationToken cancellationToken)
    {
        return _context.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .Where(m => m.Players.Any(mp => playerIds.Contains(mp.PlayerId)))
            .ToListAsync(cancellationToken);
    }

    private static Dictionary<Guid, PlayerAccumulator> InitializePlayerAccumulators(HashSet<Guid> playerIds)
    {
        var dict = new Dictionary<Guid, PlayerAccumulator>(playerIds.Count);
        foreach (var id in playerIds)
            dict[id] = PlayerAccumulator.Empty;

        return dict;
    }

    private static void ProcessMatchForBasicStats(
        MatchEntity match,
        HashSet<Guid> trackedPlayerIds,
        Dictionary<Guid, PlayerAccumulator> perPlayer,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var participants = match.Players
            .Where(mp => trackedPlayerIds.Contains(mp.PlayerId))
            .ToList();

        if (participants.Count == 0) return;

        var outcome = GetMatchOutcome(match);

        foreach (var mp in participants)
        {
            var playerId = mp.PlayerId;

            var acc = perPlayer[playerId];
            acc.MatchesPlayed++;

            if (outcome.HasScore)
            {
                if (outcome.IsTie)
                    acc.Ties++;
                else if (outcome.WinningTeam == mp.Team)
                    acc.Wins++;
                else
                    acc.Losses++;
            }

            perPlayer[playerId] = acc;
        }

        foreach (var teamGroup in participants.GroupBy(p => p.Team))
        {
            var teamPlayers = teamGroup.Select(p => p.PlayerId).Distinct().ToList();
            if (teamPlayers.Count < 2) continue;

            var teamWon = outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == teamGroup.Key;

            for (int i = 0; i < teamPlayers.Count; i++)
            {
                for (int j = i + 1; j < teamPlayers.Count; j++)
                {
                    var key = PairKey.Create(teamPlayers[i], teamPlayers[j]);

                    if (!pairTotals.TryGetValue(key, out var pairAcc))
                        pairAcc = PairAccumulator.Empty;

                    pairAcc.MatchesTogether++;
                    if (teamWon) pairAcc.WinsTogether++;

                    pairTotals[key] = pairAcc;
                }
            }
        }
    }

    private static void ProcessMatchForVisual(
        MatchEntity match,
        HashSet<Guid> trackedPlayerIds,
        Dictionary<Guid, PlayerAccumulator> perPlayer,
        Dictionary<PairKey, PairAccumulator> pairTotals,
        Dictionary<Guid, int> mvpCounts)
    {
        var participants = match.Players
            .Where(mp => trackedPlayerIds.Contains(mp.PlayerId))
            .ToList();

        if (participants.Count == 0) return;

        // MVP counts
        foreach (var mp in participants)
        {
            if (mp.IsMvp == true)
            {
                var pid = mp.PlayerId;
                if (mvpCounts.ContainsKey(pid))
                    mvpCounts[pid]++;
            }
        }

        var outcome = GetMatchOutcome(match);

        // Individual W/D/L
        foreach (var mp in participants)
        {
            var playerId = mp.PlayerId;

            var acc = perPlayer[playerId];
            acc.MatchesPlayed++;

            if (outcome.HasScore)
            {
                if (outcome.IsTie)
                    acc.Ties++;
                else if (outcome.WinningTeam == mp.Team)
                    acc.Wins++;
                else
                    acc.Losses++;
            }

            perPlayer[playerId] = acc;
        }

        // Pair synergy
        foreach (var teamGroup in participants.GroupBy(p => p.Team))
        {
            var teamPlayers = teamGroup.Select(p => p.PlayerId).Distinct().ToList();
            if (teamPlayers.Count < 2) continue;

            var teamWon = outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == teamGroup.Key;

            for (int i = 0; i < teamPlayers.Count; i++)
            {
                for (int j = i + 1; j < teamPlayers.Count; j++)
                {
                    var key = PairKey.Create(teamPlayers[i], teamPlayers[j]);

                    if (!pairTotals.TryGetValue(key, out var pairAcc))
                        pairAcc = PairAccumulator.Empty;

                    pairAcc.MatchesTogether++;
                    if (teamWon) pairAcc.WinsTogether++;

                    pairTotals[key] = pairAcc;
                }
            }
        }
    }

    private static MatchOutcome GetMatchOutcome(MatchEntity match)
    {
        if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
            return MatchOutcome.NoScore;

        if (match.TeamAGoals.Value == match.TeamBGoals.Value)
            return MatchOutcome.Tie;

        var winningTeam = match.TeamAGoals.Value > match.TeamBGoals.Value ? (short)1 : (short)2;
        return MatchOutcome.Win(winningTeam);
    }

    private static Dictionary<Guid, double> BuildSynergyMap(
        Guid playerId,
        List<PlayerEntity> allPlayers,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var map = new Dictionary<Guid, double>(Math.Max(0, allPlayers.Count - 1));

        foreach (var other in allPlayers)
        {
            if (other.Id == playerId) continue;

            var key = PairKey.Create(playerId, other.Id);

            if (!pairTotals.TryGetValue(key, out var pairAcc) || pairAcc.MatchesTogether == 0)
            {
                map[other.Id] = 0.0;
                continue;
            }

            map[other.Id] = pairAcc.WinsTogether / (double)pairAcc.MatchesTogether;
        }

        return map;
    }

    private static List<PlayerSynergyItem> BuildSynergyVisual(
        Guid playerId,
        HashSet<Guid> allPlayerIds,
        Dictionary<Guid, string> playerNameById,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var list = new List<PlayerSynergyItem>(Math.Max(0, allPlayerIds.Count - 1));

        foreach (var otherId in allPlayerIds)
        {
            if (otherId == playerId) continue;

            var key = PairKey.Create(playerId, otherId);

            if (!pairTotals.TryGetValue(key, out var pairAcc) || pairAcc.MatchesTogether == 0)
            {
                list.Add(new PlayerSynergyItem
                {
                    WithPlayerId = otherId,
                    WithPlayerName = playerNameById.TryGetValue(otherId, out var n) ? n : otherId.ToString(),
                    MatchesTogether = 0,
                    WinsTogether = 0,
                    WinRateTogether = 0.0
                });

                continue;
            }

            list.Add(new PlayerSynergyItem
            {
                WithPlayerId = otherId,
                WithPlayerName = playerNameById.TryGetValue(otherId, out var name) ? name : otherId.ToString(),
                MatchesTogether = pairAcc.MatchesTogether,
                WinsTogether = pairAcc.WinsTogether,
                WinRateTogether = pairAcc.MatchesTogether == 0 ? 0.0 : pairAcc.WinsTogether / (double)pairAcc.MatchesTogether
            });
        }

        return list;
    }

    private readonly record struct PairKey(Guid A, Guid B)
    {
        public static PairKey Create(Guid x, Guid y)
            => x.CompareTo(y) <= 0 ? new PairKey(x, y) : new PairKey(y, x);
    }

    private sealed class PlayerAccumulator
    {
        public int MatchesPlayed;
        public int Wins;
        public int Ties;
        public int Losses;

        public static PlayerAccumulator Empty => new PlayerAccumulator();
    }

    private sealed class PairAccumulator
    {
        public int MatchesTogether;
        public int WinsTogether;

        public static PairAccumulator Empty => new PairAccumulator();
    }

    private readonly record struct MatchOutcome(bool HasScore, bool IsTie, short WinningTeam)
    {
        public static MatchOutcome NoScore => new(false, false, 0);
        public static MatchOutcome Tie => new(true, true, 0);
        public static MatchOutcome Win(short winningTeam) => new(true, false, winningTeam);
    }
}
