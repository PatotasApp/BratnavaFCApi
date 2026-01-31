using System;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Models;
using Xunit;

namespace BratnavaFC.Tests;

public class FactoryTests
{
    [Fact]
    public void Create_Returns_Correct_Strategy_Types()
    {
        var random = TeamGenerationFactory.Create(StrategyType.Random);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(random);
        Assert.IsType<RandomStrategy>(random);

        var group = TeamGenerationFactory.Create(StrategyType.GroupByWins);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(group);
        Assert.IsType<GroupByWinsStrategy>(group);

        var algorithm = TeamGenerationFactory.Create(StrategyType.Algorithm);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(algorithm);
        Assert.IsType<AlgorithmStrategy>(algorithm);

        var manual = TeamGenerationFactory.Create(StrategyType.Manual);
        Assert.IsAssignableFrom<ITeamGenerationStrategy>(manual);
        Assert.IsType<ManualStrategy>(manual);
    }
}