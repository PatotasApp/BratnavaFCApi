using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineRoundRobinWithFinalTests
{
    private static readonly Guid[] Teams = Enumerable.Range(1, 5)
        .Select(value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
        .ToArray();

    [Fact]
    public void RoundRobinWithFinal_CreatesQualifyingAndFinalForTopTwo()
    {
        var qualifying = RoundRobinWithFinalEngine.GenerateQualifying(Teams, turns: 2);
        var final = RoundRobinWithFinalEngine.CreateFinal(new[] { Teams[3], Teams[1], Teams[0], Teams[2], Teams[4] });

        Assert.Equal(10, qualifying.Count);
        Assert.Equal(new KnockoutGame(Teams[3], Teams[1]), final);
    }
}
