using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;

public static class TeamGenerationFactory
{
    public static ITeamGenerationStrategy Create(
        StrategyType type,
        IPlayerStatsService stats,
        ILoggerFactory loggerFactory)
    {
        return type switch
        {
            StrategyType.Algorithm => new AlgorithmStrategy(stats, loggerFactory.CreateLogger<AlgorithmStrategy>()),
            StrategyType.Manual => new ManualStrategy(stats),
            StrategyType.Random => new RandomStrategy(stats),
            StrategyType.GroupByWins => new GroupByWinsStrategy(stats),
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unknown strategy type")
        };
    }
}