using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.TeamGeneration;

public interface ITeamGenerationStrategy
{
    Task<TeamsResultDto> GenerateTeamsAsync(List<Player> players, TeamGenerationSettings settings);
}