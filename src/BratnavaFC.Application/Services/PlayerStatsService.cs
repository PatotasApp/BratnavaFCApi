using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BratnavaFC.Application.Services;

public sealed class PlayerStatsService : IPlayerStatsService
{
    private readonly AppDbContext _context;

    public PlayerStatsService(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerEntity> players)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (players.Count == 0) return new List<PlayerStats>();

        var playerIds = players.Select(p => p.Id).ToHashSet();
        var matches = await LoadMatchesAsync(playerIds);

        var perPlayer = InitializePlayerAccumulators(playerIds);
        var pairTotals = new Dictionary<PairKey, PairAccumulator>();

        foreach (var match in matches)
        {
            ProcessMatch(match, playerIds, perPlayer, pairTotals);
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

    private Task<List<MatchEntity>> LoadMatchesAsync(HashSet<Guid> playerIds)
    {
        return _context.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .Where(m => m.Players.Any(p => playerIds.Contains(p.Id)))
            .ToListAsync();
    }

    private static Dictionary<Guid, PlayerAccumulator> InitializePlayerAccumulators(HashSet<Guid> playerIds)
    {
        var dict = new Dictionary<Guid, PlayerAccumulator>(playerIds.Count);
        foreach (var id in playerIds)
            dict[id] = PlayerAccumulator.Empty;

        return dict;
    }

    private static void ProcessMatch(
        MatchEntity match,
        HashSet<Guid> trackedPlayerIds,
        Dictionary<Guid, PlayerAccumulator> perPlayer,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var participants = match.Players
            .Where(p => trackedPlayerIds.Contains(p.Id))
            .ToList();

        if (participants.Count == 0)
            return;

        var outcome = GetMatchOutcome(match);

        foreach (var mp in participants)
        {
            var acc = perPlayer[mp.Id];
            acc.MatchesPlayed++;

            if (outcome.IsTie)
            {
                acc.Ties++;
            }
            else
            {
                if (outcome.WinningTeam == mp.Team)
                    acc.Wins++;
                else
                    acc.Losses++;
            }

            perPlayer[mp.Id] = acc;
        }

        foreach (var teamGroup in participants.GroupBy(p => p.Team))
        {
            var teamPlayers = teamGroup.Select(p => p.Id).Distinct().ToList();
            if (teamPlayers.Count < 2) continue;

            var teamWon = !outcome.IsTie && outcome.WinningTeam == teamGroup.Key;

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
        if (match.TeamAGoals == match.TeamBGoals)
            return MatchOutcome.Tie;

        var winningTeam = match.TeamAGoals > match.TeamBGoals ? 1 : 2;
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

    private readonly record struct PairKey(Guid A, Guid B)
    {
        public static PairKey Create(Guid x, Guid y)
            => x.CompareTo(y) <= 0 ? new PairKey(x, y) : new PairKey(y, x);
    }

    // mutable accumulator used during computation
    private sealed class PlayerAccumulator
    {
        public int MatchesPlayed;
        public int Wins;
        public int Ties;
        public int Losses;

        public static PlayerAccumulator Empty => new PlayerAccumulator();
    }

    // mutable pair accumulator
    private sealed class PairAccumulator
    {
        public int MatchesTogether;
        public int WinsTogether;

        public static PairAccumulator Empty => new PairAccumulator();
    }

    private readonly record struct MatchOutcome(bool IsTie, int WinningTeam)
    {
        public static MatchOutcome Tie => new(true, 0);
        public static MatchOutcome Win(int winningTeam) => new(false, winningTeam);
    }
}
