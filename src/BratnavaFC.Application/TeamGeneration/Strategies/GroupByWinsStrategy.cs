using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task<TeamsResultDto> GenerateTeamsAsync(List<Player> players, TeamGenerationSettings settings)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        var statsByPlayerId = await LoadStatsByPlayerId(players);

        var candidates = SelectCandidates(players, statsByPlayerId, settings);
        var perTeam = settings.PlayersPerTeam;

        var result = new TeamsResultDto([], [], []);

        var maxAssignable = Math.Min(candidates.Count, perTeam * 2);
        if (maxAssignable == 0)
        {
            result.Unassigned.AddRange(players.Select(p => p.Id));
            return result;
        }

        var ordered = candidates
            .OrderByDescending(x => x.Stats.Wins)
            .ThenBy(x => x.Player.Id)
            .ToList();

        SeedTeams(ordered, result);

        var toA = true;
        for (int i = 2; i < ordered.Count && (result.TeamA.Count + result.TeamB.Count) < maxAssignable; i++)
        {
            var pick = ordered[i].Player.Id;

            if (toA && result.TeamA.Count < perTeam)
                result.TeamA.Add(pick);
            else if (!toA && result.TeamB.Count < perTeam)
                result.TeamB.Add(pick);

            toA = !toA;
        }

        var assigned = new HashSet<Guid>(result.TeamA.Concat(result.TeamB));
        result.Unassigned.AddRange(ordered.Where(x => !assigned.Contains(x.Player.Id)).Select(x => x.Player.Id));

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
        => statsByPlayerId.TryGetValue(playerId, out var s) ? s : new PlayerStats { PlayerId = playerId };

    private static void SeedTeams(List<PlayerWithStats> ordered, TeamsResultDto result)
    {
        if (ordered.Count == 0) return;
        if (ordered.Count == 1)
        {
            result.TeamA.Add(ordered[0].Player.Id);
            return;
        }

        var first = ordered[0];
        var second = ordered[1];

        if (first.Stats.Wins <= second.Stats.Wins)
        {
            result.TeamA.Add(first.Player.Id);
            result.TeamB.Add(second.Player.Id);
        }
        else
        {
            result.TeamA.Add(second.Player.Id);
            result.TeamB.Add(first.Player.Id);
        }
    }
}
