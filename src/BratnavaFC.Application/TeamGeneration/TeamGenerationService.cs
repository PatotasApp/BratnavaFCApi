using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.TeamGeneration;

public class TeamGenerationService
{
    private readonly IPlayerStatsService _playerStatsService;

    public TeamGenerationService(IPlayerStatsService playerStatsService)
    {
        _playerStatsService = playerStatsService ?? throw new ArgumentNullException(nameof(playerStatsService));
    }

    public async Task<TeamsResultDto> GenerateAsync(List<PlayerEntity> players, TeamGenerationSettings settings, StrategyType strategyType)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var strategy = TeamGenerationFactory.Create(strategyType, _playerStatsService);
        return await strategy.GenerateTeamsAsync(players, settings);
    }
}