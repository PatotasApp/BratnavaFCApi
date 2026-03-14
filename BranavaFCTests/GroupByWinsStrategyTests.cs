using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Unit tests for GroupByWinsStrategy.
///
/// Key rules exercised:
///  - Sort order is by Wins DESC (not WinRate)
///  - AlternateStartA: rank positions go A,B,A,B,... (even index → TeamA)
///  - AlternateStartB: rank positions go B,A,B,A,... (odd index → TeamA)
///  - SnakeABBA: positions 0,3,4,7,... → TeamA; positions 1,2,5,6,... → TeamB
///  - Duplicate options (same canonical team key) are deduplicated
///  - Neutral rule: &lt;3 matches → weight = 0.50
///  - GKs go to Unassigned when IncludeGoalkeepers = false
///  - Score = BalanceDiff (no synergy)
///  - Options ordered by Score ascending
/// </summary>
public class GroupByWinsStrategyTests
{
    // ----------------------------------------------------------------
    // 1. Empty input
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyPlayers_ReturnsSingleOptionWithEmptyTeams()
    {
        // GroupByWinsStrategy falls through to the maxAssignable==0 path (no early-exit for empty list),
        // so it returns exactly 1 option with empty TeamA, TeamB, and Unassigned.
        var strategy = new GroupByWinsStrategy(new FakeStatsService([]));

        var result = await strategy.GenerateTeamsAsync([], new TeamGenerationSettings { PlayersPerTeam = 5 });

        result.Options.Should().HaveCount(1);
        var opt = result.Options[0];
        opt.TeamA.Should().BeEmpty();
        opt.TeamB.Should().BeEmpty();
        opt.Unassigned.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // 2. Sort by Wins, not by WinRate
    // ----------------------------------------------------------------

    [Fact]
    public async Task SortsByWins_NotWinRate_ThenFirstOption_AlternatesFromA()
    {
        // PlayerH: 1 win but 100% WinRate (only 1 match, neutral → 0.50)
        // PlayerA: 10 wins but 50% WinRate
        // GroupByWins must put A ahead of H in sorted order
        var players = TestHelpers.Players(
            ("H", false), ("A", false), ("B", false), ("C", false));

        var ids = players.ToDictionary(p => p.Name, p => p.Id);

        var stats = new[]
        {
            TestHelpers.Stats(ids["H"], "H", wins: 1,  ties: 0, losses: 0),  // 100% WR but 1 win
            TestHelpers.Stats(ids["A"], "A", wins: 10, ties: 0, losses: 10), // 50% WR, 10 wins
            TestHelpers.Stats(ids["B"], "B", wins: 8,  ties: 0, losses: 8),
            TestHelpers.Stats(ids["C"], "C", wins: 6,  ties: 0, losses: 6),
        };

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 1);

        // Sorted by wins desc: A(10), B(8), C(6), H(1)
        // AlternateStartA (option 1): index0→TeamA, index1→TeamB, index2→TeamA, index3→TeamB
        var opt = result.Options[0];
        var teamAIds = opt.TeamA.Select(x => x.PlayerId).ToHashSet();
        var teamBIds = opt.TeamB.Select(x => x.PlayerId).ToHashSet();

        // A(rank0) and C(rank2) → TeamA
        teamAIds.Should().Contain(ids["A"], "highest-wins player goes to TeamA in AlternateStartA");
        teamAIds.Should().Contain(ids["C"]);
        // B(rank1) and H(rank3) → TeamB
        teamBIds.Should().Contain(ids["B"]);
        teamBIds.Should().Contain(ids["H"]);
    }

    // ----------------------------------------------------------------
    // 3. AlternateStartA pattern: rank 0,2,4 → TeamA; rank 1,3,5 → TeamB
    // ----------------------------------------------------------------

    [Fact]
    public async Task AlternateStartA_EvenRanksGoToTeamA()
    {
        // 6 players with clearly distinct wins: W1 > W2 > ... > W6
        var players = TestHelpers.Players(
            ("P1", false), ("P2", false), ("P3", false),
            ("P4", false), ("P5", false), ("P6", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = ids.Select((id, i) =>
            TestHelpers.Stats(id, $"P{i + 1}", wins: 10 - i, ties: 0, losses: 5));

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        // Request only 1 option → AlternateStartA
        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 1);

        var opt = result.Options[0];
        var teamAIds = opt.TeamA.Select(x => x.PlayerId).ToHashSet();

        // Sorted rank: P1(W=10), P2(W=9), ..., P6(W=5)
        // AlternateStartA: even indices (0,2,4) → TeamA → P1, P3, P5
        teamAIds.Should().Contain(ids[0], "rank-0 (most wins) → TeamA");
        teamAIds.Should().Contain(ids[2], "rank-2 → TeamA");
        teamAIds.Should().Contain(ids[4], "rank-4 → TeamA");

        var teamBIds = opt.TeamB.Select(x => x.PlayerId).ToHashSet();
        teamBIds.Should().Contain(ids[1], "rank-1 → TeamB");
        teamBIds.Should().Contain(ids[3], "rank-3 → TeamB");
        teamBIds.Should().Contain(ids[5], "rank-5 → TeamB");
    }

    // ----------------------------------------------------------------
    // 4. SnakeABBA pattern: positions 0,3 → TeamA; positions 1,2 → TeamB (per block of 4)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SnakeABBA_CorrectPattern_FirstBlock()
    {
        var players = TestHelpers.Players(
            ("P1", false), ("P2", false), ("P3", false), ("P4", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = ids.Select((id, i) =>
            TestHelpers.Stats(id, $"P{i + 1}", wins: 10 - i, ties: 0, losses: 5));

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        // Request 3 options to generate SnakeABBA as option 3
        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        // SnakeABBA: index%4: 0→A, 1→B, 2→B, 3→A
        // Sorted: P1(rank0)→A, P2(rank1)→B, P3(rank2)→B, P4(rank3)→A
        // → TeamA: P1, P4   TeamB: P2, P3
        var snakeOpt = result.Options.FirstOrDefault(o =>
        {
            var aSet = o.TeamA.Select(x => x.PlayerId).ToHashSet();
            return aSet.Contains(ids[0]) && aSet.Contains(ids[3]);
        });

        snakeOpt.Should().NotBeNull("SnakeABBA option with P1+P4 in TeamA must exist (or be deduplicated equivalent)");
    }

    // ----------------------------------------------------------------
    // 5. Deduplication: identical canonical team keys are not repeated
    // ----------------------------------------------------------------

    [Fact]
    public async Task Deduplication_RemovesDuplicateTeamOptions()
    {
        // With only 2 players and PlayersPerTeam=1, all three modes yield the same split
        var players = TestHelpers.Players(("A", false), ("B", false));
        var ids = players.Select(p => p.Id).ToList();
        var stats = new[]
        {
            TestHelpers.Stats(ids[0], "A", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[1], "B", wins: 5, ties: 0, losses: 5),
        };

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 1, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        // Only 1 unique split is possible, so deduplication must yield exactly 1 option
        result.Options.Should().HaveCount(1, "duplicate team compositions must be deduplicated");
    }

    // ----------------------------------------------------------------
    // 6. Neutral weight: player with < 3 matches gets weight 0.50
    // ----------------------------------------------------------------

    [Fact]
    public async Task NeutralPlayer_GetsWeight0_50()
    {
        var players = TestHelpers.Players(
            ("New", false), ("A", false), ("B", false), ("C", false));

        var ids = players.ToDictionary(p => p.Name, p => p.Id);

        var stats = new[]
        {
            TestHelpers.NeutralStats(ids["New"], "New"),  // 0 matches → neutral
            TestHelpers.Stats(ids["A"], "A", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids["B"], "B", wins: 4, ties: 0, losses: 6),
            TestHelpers.Stats(ids["C"], "C", wins: 3, ties: 0, losses: 7),
        };

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 1);

        var newEntry = result.Options
            .SelectMany(o => o.TeamA.Concat(o.TeamB).Concat(o.Unassigned))
            .First(p => p.PlayerId == ids["New"]);

        newEntry.Weight.Should().BeApproximately(0.50, 1e-9,
            "player with < 3 matches must receive neutral weight 0.50");
    }

    // ----------------------------------------------------------------
    // 7. GK exclusion: IncludeGoalkeepers = false
    // ----------------------------------------------------------------

    [Fact]
    public async Task GoalkeeperExclusion_GKsGoToUnassigned_WhenSettingFalse()
    {
        var players = TestHelpers.Players(
            ("GK", true), ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.ToDictionary(p => p.Name, p => p.Id);
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 1);

        foreach (var opt in result.Options)
        {
            var teamIds = opt.TeamA.Select(x => x.PlayerId)
                            .Concat(opt.TeamB.Select(x => x.PlayerId))
                            .ToHashSet();

            teamIds.Should().NotContain(ids["GK"], "GK must not be assigned when IncludeGoalkeepers=false");
            opt.Unassigned.Select(x => x.PlayerId).Should().Contain(ids["GK"]);
        }
    }

    // ----------------------------------------------------------------
    // 8. Score = BalanceDiff (no synergy in this strategy)
    // ----------------------------------------------------------------

    [Fact]
    public async Task Score_EqualsBalanceDiff_ForAllOptions()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = new[]
        {
            TestHelpers.Stats(ids[0], "A", wins: 8, ties: 0, losses: 2),
            TestHelpers.Stats(ids[1], "B", wins: 6, ties: 0, losses: 4),
            TestHelpers.Stats(ids[2], "C", wins: 4, ties: 0, losses: 6),
            TestHelpers.Stats(ids[3], "D", wins: 2, ties: 0, losses: 8),
        };

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            opt.Score.Should().BeApproximately(opt.BalanceDiff, 1e-9,
                "GroupByWinsStrategy sets Score = BalanceDiff (no synergy)");
        }
    }

    // ----------------------------------------------------------------
    // 9. Options ordered by Score ascending
    // ----------------------------------------------------------------

    [Fact]
    public async Task Options_OrderedByScore_Ascending()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = ids.Select((id, i) =>
            TestHelpers.Stats(id, $"P{i}", wins: 10 - i * 2, ties: 0, losses: i * 2 + 3));

        var strategy = new GroupByWinsStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        result.Options.Select(o => o.Score).Should().BeInAscendingOrder(
            because: "options must be sorted by Score ascending");
    }
}
