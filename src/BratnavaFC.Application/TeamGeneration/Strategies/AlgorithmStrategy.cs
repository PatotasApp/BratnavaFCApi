using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration;

public sealed class AlgorithmStrategy : ITeamGenerationStrategy
{
    private const double NeutralSynergy = 0.5;
    private readonly IPlayerStatsService _statsService;

    public AlgorithmStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

    public async Task<TeamsResultDto> GenerateTeamsAsync(List<Player> players, TeamGenerationSettings settings)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        var statsByPlayerId = await LoadStatsByPlayerId(players);

        var candidates = SelectCandidates(players, statsByPlayerId, settings);
        var perTeam = settings.PlayersPerTeam;
        var result = new TeamsResultDto();

        var maxAssignable = Math.Min(candidates.Count, perTeam * 2);
        if (maxAssignable == 0)
        {
            result.Unassigned.AddRange(players.Select(p => p.Id));
            return result;
        }

        SeedTeams(candidates, out var teamA, out var teamB);

        var waiting = candidates.ToList();

        var pickForA = true;
        while (waiting.Count > 0 && (teamA.Count + teamB.Count) < maxAssignable)
        {
            var target = pickForA ? teamA : teamB;

            if (target.Count >= perTeam)
            {
                pickForA = !pickForA;
                if (teamA.Count >= perTeam && teamB.Count >= perTeam) break;
                continue;
            }

            var best = SelectBestCandidate(waiting, target);
            target.Add(best);
            waiting.Remove(best);

            pickForA = !pickForA;
        }

        result.TeamA.AddRange(teamA.Select(x => x.Player.Id));
        result.TeamB.AddRange(teamB.Select(x => x.Player.Id));
        result.Unassigned.AddRange(waiting.Select(x => x.Player.Id));

        if (!settings.IncludeGoalkeepers)
            result.Unassigned.AddRange(players.Where(p => p.IsGoalkeeper).Select(p => p.Id));

        return result;
    }

    private async Task<Dictionary<Guid, PlayerStats>> LoadStatsByPlayerId(List<Player> players)
    {
        var statsList = await _statsService.EnrichPlayersAsync(players);
        return statsList.ToDictionary(s => s.PlayerId, s => s);
    }

    private static List<PlayerWithStats> SelectCandidates(
        List<Player> players,
        Dictionary<Guid, PlayerStats> statsByPlayerId,
        TeamGenerationSettings settings)
    {
        var selected = settings.IncludeGoalkeepers
            ? players
            : players.Where(p => !p.IsGoalkeeper);

        return selected
            .Select(p => new PlayerWithStats(p, GetOrCreateStats(statsByPlayerId, p.Id)))
            .ToList();
    }

    private static PlayerStats GetOrCreateStats(Dictionary<Guid, PlayerStats> statsByPlayerId, Guid playerId)
        => statsByPlayerId.TryGetValue(playerId, out var s)
            ? s
            : new PlayerStats { PlayerId = playerId, Wins = 0, Ties = 0, Losses = 0, WinRate = 0.0, SynergyWith = new() };

    private static void SeedTeams(List<PlayerWithStats> ordered, out List<PlayerWithStats> teamA, out List<PlayerWithStats> teamB)
    {
        teamA = new List<PlayerWithStats>();
        teamB = new List<PlayerWithStats>();

        if (ordered.Count == 0) return;

        var byWinRate = ordered.OrderByDescending(x => x.Stats.WinRate).ToList();
        var key1 = byWinRate[0];
        PlayerWithStats? key2 = byWinRate.Count > 1 ? byWinRate[1] : null;

        if (key2 is null)
        {
            teamA.Add(key1);
            ordered.Remove(key1);

            if (ordered.Count > 0)
            {
                var next = ordered.OrderByDescending(x => x.Stats.WinRate).First();
                teamB.Add(next);
                ordered.Remove(next);
            }

            return;
        }

        var first = key1.Stats.WinRate <= key2.Value.Stats.WinRate ? key1 : key2.Value;
        var second = first.Equals(key1) ? key2.Value : key1;

        teamA.Add(first);
        teamB.Add(second);

        ordered.Remove(first);
        ordered.Remove(second);
    }

    private static PlayerWithStats SelectBestCandidate(List<PlayerWithStats> waiting, List<PlayerWithStats> team)
    {
        if (team.Count == 0)
        {
            return waiting
                .OrderByDescending(p => p.Stats?.WinRate ?? 0.0)
                .ThenBy(p => p.Player.Id)
                .First();
        }

        var teamStats = team.Select(t => t.Stats).ToList();
        var statsLookup = teamStats.ToDictionary(s => s.PlayerId, s => s);

        return waiting
            .OrderByDescending(p => ComputeSynergySum(p.Stats, teamStats, statsLookup))
            .ThenByDescending(p => p.Stats?.WinRate ?? 0.0)
            .ThenBy(p => p.Player.Id)
            .First();
    }

    private static double ComputeSynergySum(PlayerStats candidateStats, List<PlayerStats> teamStats, Dictionary<Guid, PlayerStats> statsByPlayerId)
    {
        if (teamStats == null || teamStats.Count == 0) return candidateStats?.WinRate ?? 0.0;
        double sum = 0;
        foreach (var member in teamStats)
        {
            sum += GetPairSynergy(candidateStats, member.PlayerId, statsByPlayerId);
        }

        return sum;
    }

    private static double GetPairSynergy(
        PlayerStats candidateStats,
        Guid memberId,
        Dictionary<Guid, PlayerStats> statsByPlayerId)
    {
        if (candidateStats.SynergyWith != null && candidateStats.SynergyWith.TryGetValue(memberId, out var v))
            return Clamp01(v);

        if (statsByPlayerId.TryGetValue(memberId, out var memberStats) &&
            memberStats.SynergyWith != null && memberStats.SynergyWith.TryGetValue(candidateStats.PlayerId, out var v2))
            return Clamp01(v2);

        return NeutralSynergy;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}