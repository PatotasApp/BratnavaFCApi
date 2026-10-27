using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineStandingsTests
{
    private static readonly Guid TeamA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TeamC = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid TeamD = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly TiebreakerCriteria[] DefaultCriteria =
    {
        TiebreakerCriteria.Points,
        TiebreakerCriteria.GoalDifference,
        TiebreakerCriteria.GoalsFor,
        TiebreakerCriteria.HeadToHead,
        TiebreakerCriteria.ExternalDecision
    };

    [Fact]
    public void Calculate_UsesConfiguredWinAndDrawPoints()
    {
        var rules = new StandingsRules(StandingMode.TotalPoints, 2, 1, DefaultCriteria);
        var games = new[]
        {
            new ChampionshipMatch(TeamA, TeamB, 1, 0),
            new ChampionshipMatch(TeamA, TeamC, 0, 0)
        };

        var standings = StandingsCalculator.Calculate(new[] { TeamA, TeamB, TeamC }, games, rules);

        var teamA = standings.Single(item => item.TeamId == TeamA);
        Assert.Equal(3, teamA.Points);
        Assert.Equal(1, teamA.Wins);
        Assert.Equal(1, teamA.Draws);
        Assert.Equal(1, teamA.Position);
    }

    [Fact]
    public void Calculate_PointsPerGame_CanRankTeamWithFewerTotalPointsFirst()
    {
        var rules = new StandingsRules(StandingMode.PointsPerGame, 3, 1, DefaultCriteria);
        var games = new[]
        {
            new ChampionshipMatch(TeamA, TeamC, 1, 0),
            new ChampionshipMatch(TeamB, TeamC, 1, 0),
            new ChampionshipMatch(TeamB, TeamD, 0, 0)
        };

        var standings = StandingsCalculator.Calculate(new[] { TeamA, TeamB, TeamC, TeamD }, games, rules);

        Assert.Equal(TeamA, standings[0].TeamId);
        Assert.Equal(3m, standings[0].ClassificationPoints);
        Assert.Equal(TeamB, standings[1].TeamId);
        Assert.Equal(2m, standings[1].ClassificationPoints);
        Assert.Equal(4, standings[1].Points);
    }

    [Fact]
    public void Calculate_HeadToHead_BreaksOtherwiseCompleteTie()
    {
        var rules = new StandingsRules(StandingMode.TotalPoints, 3, 1, DefaultCriteria);
        var games = new[]
        {
            new ChampionshipMatch(TeamA, TeamB, 1, 0),
            new ChampionshipMatch(TeamC, TeamA, 1, 0),
            new ChampionshipMatch(TeamB, TeamD, 1, 0)
        };

        var standings = StandingsCalculator.Calculate(new[] { TeamB, TeamA, TeamC, TeamD }, games, rules);

        var teamA = standings.Single(item => item.TeamId == TeamA);
        var teamB = standings.Single(item => item.TeamId == TeamB);
        Assert.Equal(teamA.Points, teamB.Points);
        Assert.Equal(teamA.GoalDifference, teamB.GoalDifference);
        Assert.Equal(teamA.GoalsFor, teamB.GoalsFor);
        Assert.True(teamA.Position < teamB.Position);
    }

    [Fact]
    public void Calculate_ExternalDecision_ResolvesRemainingTie()
    {
        var rules = new StandingsRules(StandingMode.TotalPoints, 3, 1, DefaultCriteria);
        var games = new[] { new ChampionshipMatch(TeamA, TeamB, 0, 0) };

        var standings = StandingsCalculator.Calculate(
            new[] { TeamA, TeamB },
            games,
            rules,
            externalDecisionOrder: new[] { TeamB, TeamA });

        Assert.Equal(TeamB, standings[0].TeamId);
        Assert.Equal(1, standings[0].Position);
        Assert.Equal(2, standings[1].Position);
    }

    [Fact]
    public void Calculate_WithoutExternalDecision_KeepsSharedPosition()
    {
        var rules = new StandingsRules(StandingMode.TotalPoints, 3, 1, DefaultCriteria);

        var standings = StandingsCalculator.Calculate(
            new[] { TeamA, TeamB },
            new[] { new ChampionshipMatch(TeamA, TeamB, 0, 0) },
            rules);

        Assert.All(standings, item => Assert.Equal(1, item.Position));
    }
}
