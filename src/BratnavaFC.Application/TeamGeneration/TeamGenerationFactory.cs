using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BratnavaFC.Application.TeamGeneration;

public static class TeamGenerationFactory
{
    public static ITeamGenerationStrategy Create(
        StrategyType type,
        IPlayerStatsService statsService,
        ILoggerFactory? loggerFactory = null)
    {
        ILogger<T> CreateLogger<T>() =>
            loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;

        return type switch
        {
            StrategyType.Manual => new ManualStrategy(),
            StrategyType.Random => new RandomStrategy(),
            StrategyType.Algorithm => new AlgorithmStrategy(statsService, CreateLogger<AlgorithmStrategy>()),
            StrategyType.GroupByWins => new GroupByWinsStrategy(statsService),
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unknown strategy type")
        };
    }
}
