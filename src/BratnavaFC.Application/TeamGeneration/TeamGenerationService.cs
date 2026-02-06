using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BratnavaFC.Application.TeamGeneration;

public class TeamGenerationService
{
    private readonly IPlayerStatsService _statsService;
    private readonly ILoggerFactory _loggerFactory;

    public TeamGenerationService(IPlayerStatsService statsService, ILoggerFactory loggerFactory)
    {
        _statsService = statsService;
        _loggerFactory = loggerFactory;
    }

    public async Task<TeamsResultDto> GenerateAsync(List<PlayerEntity> players, TeamGenerationSettings settings, StrategyType strategyType)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var strategy = TeamGenerationFactory.Create(strategyType, _statsService, _loggerFactory);
        return await strategy.GenerateTeamsAsync(players, settings);
    }
}