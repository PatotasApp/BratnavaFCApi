using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;
using BratnavaFC.Application.Abstractions;

namespace BratnavaFC.Application.TeamGeneration;

public static class TeamGenerationFactory
{
    public static ITeamGenerationStrategy Create(StrategyType type, IPlayerStatsService? statsService = null)
    {
        var svc = statsService ?? new DefaultPlayerStatsService();

        return type switch
        {
            StrategyType.Manual => new ManualStrategy(),
            StrategyType.Random => new RandomStrategy(),
            StrategyType.Algorithm => new AlgorithmStrategy(svc),
            StrategyType.GroupByWins => new GroupByWinsStrategy(svc),
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unknown strategy type")
        };
    }

    private sealed class DefaultPlayerStatsService : IPlayerStatsService
    {
        public Task<List<PlayerStats>> EnrichPlayersAsync(List<Player> players)
        {
            var list = players.Select(p => new PlayerStats
            {
                PlayerId = p.Id
            }).ToList();
            return Task.FromResult(list);
        }
    }
}