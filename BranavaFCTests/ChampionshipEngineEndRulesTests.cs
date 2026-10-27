using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineEndRulesTests
{
    private static readonly Guid TeamA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TeamC = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void GameShouldEnd_WithTimeAndGoals_EndsWhenEitherLimitIsReached()
    {
        var options = new GameEndOptions(TimeSpan.FromMinutes(10), goalLimit: 2);

        Assert.True(GameEndEvaluator.ShouldEnd(options, TimeSpan.FromMinutes(4), 2, 0));
        Assert.True(GameEndEvaluator.ShouldEnd(options, TimeSpan.FromMinutes(10), 0, 0));
        Assert.False(GameEndEvaluator.ShouldEnd(options, TimeSpan.FromMinutes(9), 1, 0));
    }

    [Fact]
    public void ChampionshipShouldEnd_ByNumberOfGames_UsesConfiguredLimit()
    {
        var options = ChampionshipEndOptions.ByNumberOfGames(2);
        var games = new[]
        {
            new ChampionshipMatch(TeamA, TeamB, 1, 0),
            new ChampionshipMatch(TeamB, TeamC, 0, 0)
        };

        Assert.True(ChampionshipEndEvaluator.ShouldEnd(
            options,
            new[] { TeamA, TeamB, TeamC },
            games,
            TimeSpan.Zero));
    }

    [Fact]
    public void ChampionshipShouldEnd_WhenAllPlayedSameAmount_RequiresPositiveEqualCounts()
    {
        var options = ChampionshipEndOptions.WhenAllTeamsHaveEqualGames();
        var teams = new[] { TeamA, TeamB, TeamC };
        var incomplete = new[]
        {
            new ChampionshipMatch(TeamA, TeamB, 1, 0),
            new ChampionshipMatch(TeamA, TeamC, 0, 1)
        };
        var complete = incomplete.Append(new ChampionshipMatch(TeamB, TeamC, 2, 2));

        Assert.False(ChampionshipEndEvaluator.ShouldEnd(options, teams, Array.Empty<ChampionshipMatch>(), TimeSpan.Zero));
        Assert.False(ChampionshipEndEvaluator.ShouldEnd(options, teams, incomplete, TimeSpan.Zero));
        Assert.True(ChampionshipEndEvaluator.ShouldEnd(options, teams, complete, TimeSpan.Zero));
    }
}
