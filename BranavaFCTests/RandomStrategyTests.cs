using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Unit tests for RandomStrategy.
///
/// Key rules exercised:
///  - Empty input → empty options list
///  - No player appears in both TeamA and TeamB within the same option
///  - Every player is in exactly one bucket: TeamA, TeamB, or Unassigned
///  - Team sizes match PlayersPerTeam setting (when enough players available)
///  - GKs go to Unassigned when IncludeGoalkeepers = false
///  - Options are ordered by BalanceDiff ascending
///  - At most optionsCount options returned
///  - BalanceDiff is always consistent with TeamAWeight and TeamBWeight
///  - Score = BalanceDiff (no synergy)
/// </summary>
public class RandomStrategyTests
{
    // ----------------------------------------------------------------
    // 1. Empty input
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyPlayers_ReturnsEmptyOptions()
    {
        var strategy = new RandomStrategy(new FakeStatsService([]));

        var result = await strategy.GenerateTeamsAsync(
            [], new TeamGenerationSettings { PlayersPerTeam = 5 });

        result.Options.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // 2. No duplicates within an option
    // ----------------------------------------------------------------

    [Fact]
    public async Task NoDuplicatesWithinOption_NoPlayerInBothTeams()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false));

        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            var teamAIds = opt.TeamA.Select(x => x.PlayerId).ToHashSet();
            var teamBIds = opt.TeamB.Select(x => x.PlayerId).ToHashSet();

            teamAIds.Intersect(teamBIds).Should().BeEmpty(
                "no player may appear in both TeamA and TeamB");
        }
    }

    // ----------------------------------------------------------------
    // 3. All players accounted for in exactly one bucket
    // ----------------------------------------------------------------

    [Fact]
    public async Task AllPlayers_AccountedFor_InExactlyOneBucket()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false), ("G", false));

        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        var allIds = players.Select(p => p.Id).ToHashSet();

        foreach (var opt in result.Options)
        {
            var seen = opt.TeamA.Select(x => x.PlayerId)
                          .Concat(opt.TeamB.Select(x => x.PlayerId))
                          .Concat(opt.Unassigned.Select(x => x.PlayerId))
                          .ToList();

            seen.Should().OnlyHaveUniqueItems("no player may appear in multiple buckets");
            seen.ToHashSet().Should().BeEquivalentTo(allIds, "every player must be in exactly one bucket");
        }
    }

    // ----------------------------------------------------------------
    // 4. Correct team sizes
    // ----------------------------------------------------------------

    [Fact]
    public async Task CorrectTeamSizes_WhenEnoughPlayersAvailable()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false));

        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            opt.TeamA.Should().HaveCount(3, "TeamA must have exactly PlayersPerTeam players");
            opt.TeamB.Should().HaveCount(3, "TeamB must have exactly PlayersPerTeam players");
        }
    }

    // ----------------------------------------------------------------
    // 5. GK exclusion: IncludeGoalkeepers = false
    // ----------------------------------------------------------------

    [Fact]
    public async Task GoalkeeperExclusion_GKsGoToUnassigned_WhenSettingFalse()
    {
        var players = TestHelpers.Players(
            ("GK1", true), ("GK2", true),
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.ToDictionary(p => p.Name, p => p.Id);
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));

        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            var teamIds = opt.TeamA.Select(x => x.PlayerId)
                            .Concat(opt.TeamB.Select(x => x.PlayerId))
                            .ToHashSet();

            teamIds.Should().NotContain(ids["GK1"], "GK1 must not be in a team");
            teamIds.Should().NotContain(ids["GK2"], "GK2 must not be in a team");

            opt.Unassigned.Select(x => x.PlayerId).Should().Contain(ids["GK1"]);
            opt.Unassigned.Select(x => x.PlayerId).Should().Contain(ids["GK2"]);
        }
    }

    // ----------------------------------------------------------------
    // 6. Options ordered by BalanceDiff ascending (Score = BalanceDiff)
    // ----------------------------------------------------------------

    [Fact]
    public async Task Options_OrderedByBalanceDiff_Ascending()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = new[]
        {
            TestHelpers.Stats(ids[0], "A", wins: 10, ties: 0, losses: 0),
            TestHelpers.Stats(ids[1], "B", wins: 8,  ties: 0, losses: 2),
            TestHelpers.Stats(ids[2], "C", wins: 6,  ties: 0, losses: 4),
            TestHelpers.Stats(ids[3], "D", wins: 4,  ties: 0, losses: 6),
            TestHelpers.Stats(ids[4], "E", wins: 2,  ties: 0, losses: 8),
            TestHelpers.Stats(ids[5], "F", wins: 0,  ties: 0, losses: 10),
        };

        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        result.Options.Select(o => o.BalanceDiff).Should().BeInAscendingOrder(
            because: "options must be ordered by BalanceDiff ascending");
    }

    // ----------------------------------------------------------------
    // 7. At most optionsCount options returned
    // ----------------------------------------------------------------

    [Fact]
    public async Task ReturnsAtMostRequestedOptionsCount()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 2);

        result.Options.Count.Should().BeLessThanOrEqualTo(2,
            "RandomStrategy must never return more options than requested");
    }

    // ----------------------------------------------------------------
    // 8. BalanceDiff consistent with TeamAWeight and TeamBWeight
    // ----------------------------------------------------------------

    [Fact]
    public async Task BalanceDiff_AlwaysConsistentWithWeights()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = new[]
        {
            TestHelpers.Stats(ids[0], "A", wins: 7, ties: 0, losses: 3),
            TestHelpers.Stats(ids[1], "B", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[2], "C", wins: 3, ties: 0, losses: 7),
            TestHelpers.Stats(ids[3], "D", wins: 5, ties: 0, losses: 5),
        };

        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            var expected = Math.Abs(opt.TeamAWeight - opt.TeamBWeight);
            opt.BalanceDiff.Should().BeApproximately(expected, 1e-9,
                "BalanceDiff must equal |TeamAWeight - TeamBWeight|");
        }
    }

    // ----------------------------------------------------------------
    // 9. Score = BalanceDiff (no synergy)
    // ----------------------------------------------------------------

    [Fact]
    public async Task Score_EqualsBalanceDiff_ForAllOptions()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = ids.Select((id, i) =>
            TestHelpers.Stats(id, $"P{i}", wins: i + 3, ties: 0, losses: 5));

        var strategy = new RandomStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            opt.Score.Should().BeApproximately(opt.BalanceDiff, 1e-9,
                "RandomStrategy sets Score = BalanceDiff (no synergy)");
        }
    }
}
