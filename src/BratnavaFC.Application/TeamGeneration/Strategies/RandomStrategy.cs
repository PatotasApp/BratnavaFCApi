using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.TeamGeneration;

public class RandomStrategy : ITeamGenerationStrategy
{
    public Task<TeamsResultDto> GenerateTeamsAsync(List<PlayerEntity> players, TeamGenerationSettings settings)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        var candidates = settings.IncludeGoalkeepers ? players.ToList() : players.Where(p => !p.IsGoalkeeper).ToList();

        var excludedPlayerIds = players.Where(p => !candidates.Any(c => c.Id == p.Id)).Select(p => p.Id).ToList();

        var rng = Random.Shared;
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var teamA = new List<Guid>();
        var teamB = new List<Guid>();
        int target = settings.PlayersPerTeam;

        for (int i = 0; i < candidates.Count && (teamA.Count < target || teamB.Count < target); i++)
        {
            if (teamA.Count < target && teamB.Count < target)
            {
                if (i % 2 == 0) teamA.Add(candidates[i].Id);
                else teamB.Add(candidates[i].Id);
            }
            else if (teamA.Count < target)
            {
                teamA.Add(candidates[i].Id);
            }
            else if (teamB.Count < target)
            {
                teamB.Add(candidates[i].Id);
            }
        }

        var unassignedFromCandidates = candidates.Skip(teamA.Count + teamB.Count).Select(p => p.Id).ToList();

        var unassigned = unassignedFromCandidates.Concat(excludedPlayerIds).ToList();

        var result = new TeamsResultDto(teamA, teamB, unassigned);
        return Task.FromResult(result);
    }
}