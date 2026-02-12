using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BratnavaFC.Application.Services;

public sealed class PlayerStatsService : IPlayerStatsService
{
    private readonly AppDbContext _context;

    public PlayerStatsService(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PlayerStats>> EnrichPlayersAsync(
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (players.Count == 0) return new List<PlayerStats>();

        var playerIds = players.Select(p => p.Id).ToHashSet();

        // So finalized (como voce tinha)
        var matches = await LoadFinalizedMatchesAsync(playerIds, cancellationToken);

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
                Name = pl.Name,
                Wins = acc.Wins,
                Ties = acc.Ties,
                Losses = acc.Losses,
                WinRate = winRate,
                SynergyWith = synergy
            });
        }

        return result;
    }

    public async Task<PlayerVisualStatsReport> GetVisualReportAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        // (mantem do jeito que voce ja tinha; aqui usamos PlayerEntity do banco, ok)
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

        var matches = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Include(m => m.Players)
            .ToListAsync(cancellationToken);

        var matchesFiltered = matches
            .Select(m =>
            {
                var (teamA, teamB) = GetTeams(m, groupId, playerIds);
                return new { Match = m, TeamA = teamA, TeamB = teamB };
            })
            .Where(x => x.TeamA.Count > 0 || x.TeamB.Count > 0)
            .ToList();

        var totalMatches = matchesFiltered.Count;
        var totalFinalized = matchesFiltered.Count(x => x.Match.Status == MatchStatus.Finalized);
        var totalWithScore = matchesFiltered.Count(x => x.Match.TeamAGoals.HasValue && x.Match.TeamBGoals.HasValue);

        var perPlayer = InitializePlayerAccumulators(playerIds);
        var mvpCounts = playerIds.ToDictionary(id => id, _ => 0);
        var pairTotals = new Dictionary<PairKey, PairAccumulator>();

        foreach (var x in matchesFiltered)
        {
            ProcessMatchForVisual(
                x.Match,
                x.TeamA,
                x.TeamB,
                playerIds,
                perPlayer,
                pairTotals,
                mvpCounts);
        }

        var playerNameById = players.ToDictionary(p => p.Id, p => p.Name);

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

                Synergies = synergies
                    .OrderByDescending(s => s.MatchesTogether)
                    .ThenByDescending(s => s.WinRateTogether)
                    .ToList()
            });
        }

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

    // ----------------- internals -----------------

    private Task<List<MatchEntity>> LoadFinalizedMatchesAsync(HashSet<Guid> playerIds, CancellationToken cancellationToken)
    {
        return _context.Matches
            .AsNoTracking()
            .Where(m => m.Status == MatchStatus.Finalized)
            .Where(m =>
                m.TeamAPlayers.Any(mp => playerIds.Contains(mp.PlayerId)) ||
                m.TeamBPlayers.Any(mp => playerIds.Contains(mp.PlayerId)))
            .Include(m => m.TeamAPlayers)
            .Include(m => m.TeamBPlayers)
            .Include(m => m.Players)
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
        var (teamA, teamB) = GetTeams(match, match.GroupId, trackedPlayerIds);

        var participants = teamA.Concat(teamB)
            .GroupBy(p => p.PlayerId)
            .Select(g => g.First())
            .ToList();

        if (participants.Count == 0)
            return;

        var outcome = GetMatchOutcome(match);

        foreach (var mp in participants)
        {
            var playerId = mp.PlayerId;
            if (!perPlayer.TryGetValue(playerId, out var acc))
                continue;

            acc.MatchesPlayed++;

            if (outcome.HasScore)
            {
                if (outcome.IsTie)
                {
                    acc.Ties++;
                }
                else
                {
                    var isInA = teamA.Any(x => x.PlayerId == playerId);
                    var isInB = !isInA && teamB.Any(x => x.PlayerId == playerId);

                    if (isInA && outcome.WinningTeam == MatchWinningTeam.TeamA) acc.Wins++;
                    else if (isInB && outcome.WinningTeam == MatchWinningTeam.TeamB) acc.Wins++;
                    else acc.Losses++;
                }
            }

            perPlayer[playerId] = acc;
        }

        AddTeamSynergy(teamA, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamA, pairTotals);
        AddTeamSynergy(teamB, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamB, pairTotals);
    }

    private static void ProcessMatchForVisual(
        MatchEntity match,
        IReadOnlyList<MatchPlayerEntity> teamA,
        IReadOnlyList<MatchPlayerEntity> teamB,
        HashSet<Guid> trackedPlayerIds,
        Dictionary<Guid, PlayerAccumulator> perPlayer,
        Dictionary<PairKey, PairAccumulator> pairTotals,
        Dictionary<Guid, int> mvpCounts)
    {
        var participants = teamA.Concat(teamB)
            .GroupBy(p => p.PlayerId)
            .Select(g => g.First())
            .ToList();

        if (participants.Count == 0)
            return;

        foreach (var mp in participants)
        {
            if (mp.IsMvp == true && mvpCounts.ContainsKey(mp.PlayerId))
                mvpCounts[mp.PlayerId]++;
        }

        var outcome = GetMatchOutcome(match);

        foreach (var mp in participants)
        {
            var playerId = mp.PlayerId;
            if (!perPlayer.TryGetValue(playerId, out var acc))
                continue;

            acc.MatchesPlayed++;

            if (outcome.HasScore)
            {
                if (outcome.IsTie)
                {
                    acc.Ties++;
                }
                else
                {
                    var isInA = teamA.Any(x => x.PlayerId == playerId);
                    var isInB = !isInA && teamB.Any(x => x.PlayerId == playerId);

                    if (isInA && outcome.WinningTeam == MatchWinningTeam.TeamA) acc.Wins++;
                    else if (isInB && outcome.WinningTeam == MatchWinningTeam.TeamB) acc.Wins++;
                    else acc.Losses++;
                }
            }

            perPlayer[playerId] = acc;
        }

        AddTeamSynergy(teamA, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamA, pairTotals);
        AddTeamSynergy(teamB, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamB, pairTotals);
    }

    private static void AddTeamSynergy(
        IReadOnlyList<MatchPlayerEntity> teamPlayers,
        bool teamWon,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var ids = teamPlayers.Select(p => p.PlayerId).Distinct().ToList();
        if (ids.Count < 2) return;

        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                var key = PairKey.Create(ids[i], ids[j]);

                if (!pairTotals.TryGetValue(key, out var pairAcc))
                    pairAcc = PairAccumulator.Empty;

                pairAcc.MatchesTogether++;
                if (teamWon) pairAcc.WinsTogether++;

                pairTotals[key] = pairAcc;
            }
    }

    private static (List<MatchPlayerEntity> TeamA, List<MatchPlayerEntity> TeamB) GetTeams(
        MatchEntity match,
        Guid groupId,
        HashSet<Guid> trackedPlayerIds)
    {
        var teamA = (match.TeamAPlayers ?? new List<MatchPlayerEntity>())
            .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId))
            .ToList();

        var teamB = (match.TeamBPlayers ?? new List<MatchPlayerEntity>())
            .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId))
            .ToList();

        if (teamA.Count == 0 && teamB.Count == 0 && match.Players is not null && match.Players.Count > 0)
        {
            teamA = match.Players
                .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId) && mp.Team == 1)
                .ToList();

            teamB = match.Players
                .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId) && mp.Team == 2)
                .ToList();
        }

        return (teamA, teamB);
    }

    private static MatchOutcome GetMatchOutcome(MatchEntity match)
    {
        if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
            return MatchOutcome.NoScore;

        if (match.TeamAGoals.Value == match.TeamBGoals.Value)
            return MatchOutcome.Tie;

        return match.TeamAGoals.Value > match.TeamBGoals.Value
            ? MatchOutcome.Win(MatchWinningTeam.TeamA)
            : MatchOutcome.Win(MatchWinningTeam.TeamB);
    }

    private static Dictionary<Guid, double> BuildSynergyMap(
        Guid playerId,
        List<PlayerRequestDto> allPlayers,
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
                WinRateTogether = pairAcc.MatchesTogether == 0
                    ? 0.0
                    : pairAcc.WinsTogether / (double)pairAcc.MatchesTogether
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

    private enum MatchWinningTeam : byte
    {
        None = 0,
        TeamA = 1,
        TeamB = 2
    }

    private readonly record struct MatchOutcome(bool HasScore, bool IsTie, MatchWinningTeam WinningTeam)
    {
        public static MatchOutcome NoScore => new(false, false, MatchWinningTeam.None);
        public static MatchOutcome Tie => new(true, true, MatchWinningTeam.None);
        public static MatchOutcome Win(MatchWinningTeam winningTeam) => new(true, false, winningTeam);
    }
}
