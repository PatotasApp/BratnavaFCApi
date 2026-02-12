using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class ManualStrategy : ITeamGenerationStrategy
{
    public Task<TeamsResultDto> GenerateTeamsAsync(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        return Task.FromResult(
            new TeamsResultDto(new List<Guid>(), new List<Guid>(), players.Select(p => p.Id).ToList()));
    }
}
