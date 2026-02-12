using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests;

public class TeamGenerationFactoryTests
{
    [Theory]
    [InlineData(StrategyType.Manual, "ManualStrategy")]
    [InlineData(StrategyType.Random, "RandomStrategy")]
    [InlineData(StrategyType.Algorithm, "AlgorithmStrategy")]
    [InlineData(StrategyType.GroupByWins, "GroupByWinsStrategy")]
    public void Create_Returns_Correct_Strategy(StrategyType type, string expectedName)
    {
        // Arrange
        var stats = new Mock<IPlayerStatsService>(MockBehavior.Loose);

        // Act
        var strategy = TeamGenerationFactory.Create(type, stats.Object, NullLoggerFactory.Instance);

        // Assert
        Assert.NotNull(strategy);
        Assert.Equal(expectedName, strategy.GetType().Name);
    }

    [Fact]
    public void Create_Unknown_Throws()
    {
        // Arrange
        var stats = new Mock<IPlayerStatsService>(MockBehavior.Loose);
        var unknown = (StrategyType)999;

        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TeamGenerationFactory.Create(unknown, stats.Object, NullLoggerFactory.Instance));
    }
}
