using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineScheduleTests
{
    [Theory]
    [InlineData(3, 3, 3)]
    [InlineData(4, 3, 6)]
    [InlineData(5, 5, 10)]
    public void Generate_OneTurn_CreatesEveryPairOnce(int teamCount, int expectedRounds, int expectedGames)
    {
        var teams = CreateTeams(teamCount);

        var rounds = RoundRobinScheduleGenerator.Generate(teams);

        Assert.Equal(expectedRounds, rounds.Count);
        Assert.Equal(expectedGames, rounds.Sum(round => round.Games.Count));
        var pairs = rounds.SelectMany(round => round.Games)
            .Select(game => Pair(game.TeamAId, game.TeamBId))
            .ToArray();
        Assert.Equal(expectedGames, pairs.Distinct().Count());
        Assert.All(rounds.SelectMany(round => round.Games), game =>
            Assert.NotEqual(game.TeamAId, game.TeamBId));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Generate_OddTeamCount_GivesOneDifferentByePerRound(int teamCount)
    {
        var teams = CreateTeams(teamCount);

        var rounds = RoundRobinScheduleGenerator.Generate(teams);

        Assert.All(rounds, round => Assert.NotNull(round.ByeTeamId));
        Assert.Equal(teams.Order(), rounds.Select(round => round.ByeTeamId!.Value).Order());
    }

    [Fact]
    public void Generate_TwoTurns_ReversesEveryMatchInSecondTurn()
    {
        var teams = CreateTeams(4);

        var rounds = RoundRobinScheduleGenerator.Generate(teams, turns: 2);

        Assert.Equal(6, rounds.Count);
        var firstTurn = rounds.Where(round => round.Leg == 1).SelectMany(round => round.Games).ToArray();
        var secondTurn = rounds.Where(round => round.Leg == 2).SelectMany(round => round.Games).ToArray();
        Assert.All(firstTurn, first => Assert.Contains(secondTurn, second =>
            second.TeamAId == first.TeamBId && second.TeamBId == first.TeamAId));
    }

    private static Guid[] CreateTeams(int count) =>
        Enumerable.Range(1, count)
            .Select(value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
            .ToArray();

    private static string Pair(Guid first, Guid second) =>
        string.Join("/", new[] { first, second }.Order());
}
