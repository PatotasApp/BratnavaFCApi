using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineKnockoutTests
{
    private static readonly Guid[] Teams = Enumerable.Range(1, 5)
        .Select(value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
        .ToArray();

    [Fact]
    public void CreateRound_FiveTeams_CreatesTwoGamesAndOneBye()
    {
        var round = KnockoutEngine.CreateRound(Teams);

        Assert.Equal(2, round.Games.Count);
        Assert.Equal(new[] { Teams[4] }, round.AutomaticallyAdvancedTeamIds);
    }

    [Fact]
    public void CreateNextRound_UsesWinnersAndCarriesBye()
    {
        var firstRound = KnockoutEngine.CreateRound(Teams);
        var results = new[]
        {
            new ChampionshipMatch(Teams[0], Teams[1], 2, 0),
            new ChampionshipMatch(Teams[2], Teams[3], 1, 1, Teams[3])
        };

        var secondRound = KnockoutEngine.CreateNextRound(firstRound, results);

        Assert.Single(secondRound.Games);
        Assert.Equal(new KnockoutGame(Teams[0], Teams[3]), secondRound.Games[0]);
        Assert.Equal(new[] { Teams[4] }, secondRound.AutomaticallyAdvancedTeamIds);
    }

    [Fact]
    public void GetChampion_FinalWithDraw_UsesPenaltyWinner()
    {
        var final = KnockoutEngine.CreateRound(Teams.Take(2));
        var result = new ChampionshipMatch(Teams[0], Teams[1], 3, 3, Teams[1]);

        var champion = KnockoutEngine.GetChampion(final, result);

        Assert.Equal(Teams[1], champion);
    }

    [Fact]
    public void RoundRobinWithFinal_CreatesQualifyingAndFinalForTopTwo()
    {
        var qualifying = RoundRobinWithFinalEngine.GenerateQualifying(Teams, turns: 2);
        var final = RoundRobinWithFinalEngine.CreateFinal(new[] { Teams[3], Teams[1], Teams[0], Teams[2], Teams[4] });

        Assert.Equal(10, qualifying.Count);
        Assert.Equal(new KnockoutGame(Teams[3], Teams[1]), final);
    }
}
