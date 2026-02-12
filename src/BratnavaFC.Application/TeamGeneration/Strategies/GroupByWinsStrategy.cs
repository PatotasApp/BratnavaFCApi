using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration;

public sealed class GroupByWinsStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    public GroupByWinsStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

    public async Task<TeamsResultDto> GenerateTeamsAsync(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        // candidates: optionally exclude goalkeepers
        var candidatePlayers = settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

        var perTeam = settings.PlayersPerTeam;
        var maxAssignable = Math.Min(candidatePlayers.Count, perTeam * 2);

        if (maxAssignable == 0)
        {
            return new TeamsResultDto(
                new List<Guid>(),
                new List<Guid>(),
                players.Select(p => p.Id).ToList());
        }

        var statsByPlayerId = await LoadStatsByPlayerId(candidatePlayers, cancellationToken);

        var ordered = candidatePlayers
            .Select(p => new PlayerWithStats(p, GetOrCreateStats(statsByPlayerId, p.Id, p.Name)))
            .OrderByDescending(x => x.Stats.Wins)
            .ThenBy(x => x.Player.Id)
            .ToList();

        var teamA = new List<Guid>(perTeam);
        var teamB = new List<Guid>(perTeam);

        SeedTeams(ordered, teamA, teamB, perTeam);

        var toA = true;
        for (int i = 2; i < ordered.Count && (teamA.Count + teamB.Count) < maxAssignable; i++)
        {
            var pick = ordered[i].Player.Id;

            if (toA && teamA.Count < perTeam)
                teamA.Add(pick);
            else if (!toA && teamB.Count < perTeam)
                teamB.Add(pick);

            toA = !toA;
        }

        var assigned = new HashSet<Guid>(teamA.Concat(teamB));

        // Unassigned:
        // - tudo que nao entrou por falta de slot
        // - e, se IncludeGoalkeepers=false, os GKs tambem
        var unassigned = players
            .Where(p => !assigned.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();

        if (!settings.IncludeGoalkeepers)
        {
            // garante que todos os GKs estejam em unassigned
            foreach (var gk in players.Where(p => p.IsGoalkeeper).Select(p => p.Id))
                if (!unassigned.Contains(gk))
                    unassigned.Add(gk);
        }

        return new TeamsResultDto(teamA, teamB, unassigned);
    }

    private async Task<Dictionary<Guid, PlayerStats>> LoadStatsByPlayerId(
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken)
    {
        var statsList = await _statsService.EnrichPlayersAsync(players, cancellationToken);
        return statsList.ToDictionary(s => s.PlayerId, s => s);
    }

    private static PlayerStats GetOrCreateStats(
        Dictionary<Guid, PlayerStats> statsByPlayerId,
        Guid playerId,
        string? name)
        => statsByPlayerId.TryGetValue(playerId, out var s)
            ? s
            : new PlayerStats
            {
                PlayerId = playerId,
                Name = name ?? string.Empty,
                Wins = 0,
                Ties = 0,
                Losses = 0,
                WinRate = 0.0,
                SynergyWith = new Dictionary<Guid, double>()
            };

    private static void SeedTeams(
        List<PlayerWithStats> ordered,
        List<Guid> teamA,
        List<Guid> teamB,
        int perTeam)
    {
        if (ordered.Count == 0) return;

        if (ordered.Count == 1)
        {
            if (teamA.Count < perTeam)
                teamA.Add(ordered[0].Player.Id);
            return;
        }

        var first = ordered[0];
        var second = ordered[1];

        // Mantem exatamente a regra que voce tinha:
        // o com MENOS wins vai pro TeamA, o com MAIS wins vai pro TeamB
        if (first.Stats.Wins <= second.Stats.Wins)
        {
            if (teamA.Count < perTeam) teamA.Add(first.Player.Id);
            if (teamB.Count < perTeam) teamB.Add(second.Player.Id);
        }
        else
        {
            if (teamA.Count < perTeam) teamA.Add(second.Player.Id);
            if (teamB.Count < perTeam) teamB.Add(first.Player.Id);
        }
    }

    private sealed class PlayerWithStats
    {
        public PlayerRequestDto Player { get; }
        public PlayerStats Stats { get; }

        public PlayerWithStats(PlayerRequestDto player, PlayerStats stats)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }
    }
}
