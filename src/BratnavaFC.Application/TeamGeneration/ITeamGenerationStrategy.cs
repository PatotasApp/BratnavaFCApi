using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.TeamGeneration;

public interface ITeamGenerationStrategy
{
    Task<TeamsResultDto> GenerateTeamsAsync(List<PlayerEntity> players, TeamGenerationSettings settings);
}