using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class RandomStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    public RandomStrategy(IPlayerStatsService statsService)
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
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        optionsCount = Math.Max(1, optionsCount);

        if (players.Count == 0)
            return new TeamsOptionsResultDto([]);

        List<PlayerRequestDto>        pool          = FilterCandidates(players, settings);
        int                           maxAssignable = Math.Min(pool.Count, settings.PlayersPerTeam * 2);
        Dictionary<Guid, PlayerStats> statsById     = await LoadStatsByPlayerId(_statsService, pool, cancellationToken);

        double WeightOf(Guid id)
            => statsById.TryGetValue(id, out PlayerStats? s) ? EffectiveWinRate(s) : NeutralWinRate;

        bool IsGk(Guid id)
            => players.FirstOrDefault(p => p.Id == id)?.IsGoalkeeper == true;

        HashSet<string>     seen          = new HashSet<string>();
        List<TeamOptionDto> options       = new List<TeamOptionDto>();
        int                 attemptsLimit = Math.Max(20, optionsCount * 10);

        for (int attempt = 0; attempt < attemptsLimit && options.Count < optionsCount; attempt++)
        {
            List<PlayerRequestDto> shuffled = pool.OrderBy(_ => Random.Shared.Next()).ToList();
            List<PlayerRequestDto> chosen   = shuffled.Take(maxAssignable).ToList();

            List<Guid> teamAIds = chosen.Take(settings.PlayersPerTeam).Select(p => p.Id).ToList();
            List<Guid> teamBIds = chosen.Skip(settings.PlayersPerTeam).Take(settings.PlayersPerTeam).Select(p => p.Id).ToList();

            if (!seen.Add(BuildTeamsKey(teamAIds, teamBIds))) continue;

            HashSet<Guid> assigned      = new HashSet<Guid>(teamAIds.Concat(teamBIds));
            List<Guid>    unassignedIds = players.Where(p => !assigned.Contains(p.Id)).Select(p => p.Id).ToList();

            List<PlayerWeightDto> teamA      = teamAIds.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
            List<PlayerWeightDto> teamB      = teamBIds.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
            List<PlayerWeightDto> unassigned = unassignedIds.Distinct().Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();

            double teamAWeight = teamA.Sum(x => x.Weight);
            double teamBWeight = teamB.Sum(x => x.Weight);
            double balanceDiff = Math.Abs(teamAWeight - teamBWeight);

            options.Add(new TeamOptionDto(
                TeamA: teamA,
                TeamB: teamB,
                Unassigned: unassigned,
                TeamAWeight: teamAWeight,
                TeamBWeight: teamBWeight,
                BalanceDiff: balanceDiff,
                GoalkeeperDiff: settings.IncludeGoalkeepers
                    ? Math.Abs(teamAIds.Count(IsGk) - teamBIds.Count(IsGk))
                    : 0,
                SynergyTotal: 0.0,
                Score: balanceDiff
            ));
        }

        // Best first (lowest balance diff); fallback to empty result if pool was empty
        return options.Count > 0
            ? new TeamsOptionsResultDto(options.OrderBy(o => o.Score).ToList())
            : BuildEmptyResult(players);
    }
}
