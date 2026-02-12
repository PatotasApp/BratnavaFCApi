using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class RandomStrategy : ITeamGenerationStrategy
{
    public Task<TeamsResultDto> GenerateTeamsAsync(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var pool = settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

        var rnd = new Random();
        pool = pool.OrderBy(_ => rnd.Next()).ToList();

        var totalSlots = settings.PlayersPerTeam * 2;

        var chosen = pool.Take(totalSlots).ToList();
        var unassigned = players
            .Where(p => !chosen.Any(c => c.Id == p.Id))
            .Select(p => p.Id)
            .ToList();

        var teamA = chosen.Take(settings.PlayersPerTeam).Select(p => p.Id).ToList();
        var teamB = chosen.Skip(settings.PlayersPerTeam).Take(settings.PlayersPerTeam).Select(p => p.Id).ToList();

        return Task.FromResult(new TeamsResultDto(teamA, teamB, unassigned));
    }
}
