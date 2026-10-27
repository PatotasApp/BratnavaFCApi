using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineEndRulesTests
{
    // O fim do campeonato não é automático (o admin encerra pelo botão); só o fim de cada jogo é regra.
    [Fact]
    public void GameShouldEnd_WithTimeAndGoals_EndsWhenEitherLimitIsReached()
    {
        var options = new GameEndOptions(TimeSpan.FromMinutes(10), goalLimit: 2);

        Assert.True(GameEndEvaluator.ShouldEnd(options, TimeSpan.FromMinutes(4), 2, 0));
        Assert.True(GameEndEvaluator.ShouldEnd(options, TimeSpan.FromMinutes(10), 0, 0));
        Assert.False(GameEndEvaluator.ShouldEnd(options, TimeSpan.FromMinutes(9), 1, 0));
    }
}
