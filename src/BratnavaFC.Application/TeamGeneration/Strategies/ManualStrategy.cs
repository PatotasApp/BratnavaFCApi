using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.TeamGeneration;

public class ManualStrategy : ITeamGenerationStrategy
{
    public Task<TeamsResultDto> GenerateTeamsAsync(List<PlayerEntity> players, TeamGenerationSettings settings)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        var result = new TeamsResultDto([], [], []);


        result.Unassigned.AddRange(players.Select(p => p.Id));
        return Task.FromResult(result);
    }
}