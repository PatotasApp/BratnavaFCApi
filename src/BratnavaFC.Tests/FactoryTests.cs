using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace BratnavaFC.Tests;

public class FactoryTests
{
    private static IPlayerStatsService MockStats()   => Substitute.For<IPlayerStatsService>();
    private static ILoggerFactory MockLoggerFactory()
    {
        var factory = Substitute.For<ILoggerFactory>();
        factory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
        return factory;
    }

    [Fact]
    public void Create_Returns_Correct_Strategy_Types()
    {
        var stats   = MockStats();
        var loggers = MockLoggerFactory();

        var random = TeamGenerationFactory.Create(StrategyType.Random, stats, loggers);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(random);
        Assert.IsType<RandomStrategy>(random);

        var group = TeamGenerationFactory.Create(StrategyType.GroupByWins, stats, loggers);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(group);
        Assert.IsType<GroupByWinsStrategy>(group);

        var algorithm = TeamGenerationFactory.Create(StrategyType.Algorithm, stats, loggers);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(algorithm);
        Assert.IsType<AlgorithmStrategy>(algorithm);

        var manual = TeamGenerationFactory.Create(StrategyType.Manual, stats, loggers);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(manual);
        Assert.IsType<ManualStrategy>(manual);
    }
}
