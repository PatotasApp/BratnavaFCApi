using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class ManualStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    public ManualStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

    public async Task<TeamsOptionsResultDto> GenerateTeamsAsync(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings,
        int optionsCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        if (players.Count == 0)
            return new TeamsOptionsResultDto([]);

        var statsResult = await LoadStatsByPlayerId(
            _statsService, FilterCandidates(players, settings), cancellationToken);
        if (!statsResult.Success)
            return new TeamsOptionsResultDto([]);
        Dictionary<Guid, PlayerStats> statsById = statsResult.Data!;

        // All players are unassigned — the caller assigns them manually via the UI
        List<PlayerWeightDto> unassigned = players
            .Select(p => new PlayerWeightDto(
                p.Id,
                statsById.TryGetValue(p.Id, out PlayerStats? s) ? EffectiveWeight(s) : NeutralWinRate))
            .ToList();

        // Always returns exactly one option (even if optionsCount > 1)
        return new TeamsOptionsResultDto(
        [
            new(TeamA: [], TeamB: [], Unassigned: unassigned,
                TeamAWeight: 0, TeamBWeight: 0, BalanceDiff: 0,
                SynergyTotal: 0, Score: 0)
        ]);
    }
}
