using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.TeamGeneration;

public sealed class TeamGenerationService
{
    private readonly IPlayerStatsService _statsService;
    private readonly ILoggerFactory _loggerFactory;

    public TeamGenerationService(IPlayerStatsService statsService, ILoggerFactory loggerFactory)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task<Result<TeamsOptionsResultDto>> GenerateAsync(
        List<PlayerRequestDto> players,
        StrategyType strategyType,
        int playersPerTeam,
        bool includeGoalkeepers,
        int optionsCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));

        var settings = new TeamGenerationSettings
        {
            PlayersPerTeam     = playersPerTeam,
            IncludeGoalkeepers = includeGoalkeepers,
        };

        var strategy = TeamGenerationFactory.Create(strategyType, _statsService, _loggerFactory);
        var dto = await strategy.GenerateTeamsAsync(players, settings, optionsCount, cancellationToken);
        return Result<TeamsOptionsResultDto>.Ok(dto);
    }

    public async Task<Result<TeamsOptionsResultDto>> GenerateAsync(
        TeamGenerationRequestDto request,
        int optionsCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return Result<TeamsOptionsResultDto>.Fail("Request inválido.", ResultStatus.BadRequest);
        if (request.Players is null)
            return Result<TeamsOptionsResultDto>.Fail("Players é obrigatório.", ResultStatus.BadRequest);

        return await GenerateAsync(
            request.Players,
            request.StrategyType,
            request.PlayersPerTeam,
            request.IncludeGoalkeepers,
            optionsCount,
            cancellationToken);
    }
}