using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Unit tests for ManualStrategy.
///
/// Key rules exercised:
///  - Empty input → empty options list
///  - Always returns exactly 1 option regardless of optionsCount
///  - TeamA and TeamB are always empty (manual assignment)
///  - Every player appears in Unassigned
///  - Player weight = EffectiveWinRate (real WinRate if >= 3 matches, else 0.50)
///  - GKs that are excluded from candidatePlayers still get weight 0.50 in Unassigned
/// </summary>
public class ManualStrategyTests
{
    // ----------------------------------------------------------------
    // 1. Empty input
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyPlayers_ReturnsEmptyOptions()
    {
        var strategy = new ManualStrategy(new FakeStatsService([]));

        var result = await strategy.GenerateTeamsAsync([], new TeamGenerationSettings());

        result.Options.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // 2. Always returns exactly 1 option
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task AlwaysReturnsExactlyOneOption_RegardlessOfOptionsCount(int optionsCount)
    {
        var players = TestHelpers.Players(("A", false), ("B", false), ("C", false));
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));

        var strategy = new ManualStrategy(new FakeStatsService(stats));

        var result = await strategy.GenerateTeamsAsync(players, new TeamGenerationSettings(), optionsCount);

        result.Options.Should().HaveCount(1, "ManualStrategy always returns exactly 1 option");
    }

    // ----------------------------------------------------------------
    // 3. TeamA and TeamB are always empty
    // ----------------------------------------------------------------

    [Fact]
    public async Task TeamAAndTeamB_AreEmpty()
    {
        var players = TestHelpers.Players(("A", false), ("B", false), ("C", false));
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));

        var strategy = new ManualStrategy(new FakeStatsService(stats));

        var result = await strategy.GenerateTeamsAsync(players, new TeamGenerationSettings());

        var opt = result.Options[0];
        opt.TeamA.Should().BeEmpty("ManualStrategy leaves TeamA empty for manual assignment");
        opt.TeamB.Should().BeEmpty("ManualStrategy leaves TeamB empty for manual assignment");
    }

    // ----------------------------------------------------------------
    // 4. All players appear in Unassigned
    // ----------------------------------------------------------------

    [Fact]
    public async Task AllPlayers_AppearInUnassigned()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var strategy = new ManualStrategy(new FakeStatsService(stats));

        var result = await strategy.GenerateTeamsAsync(players, new TeamGenerationSettings());

        var unassignedIds = result.Options[0].Unassigned.Select(x => x.PlayerId).ToHashSet();

        foreach (var p in players)
            unassignedIds.Should().Contain(p.Id, $"player {p.Name} must be in Unassigned");
    }

    // ----------------------------------------------------------------
    // 5. Non-neutral player gets real WinRate as weight
    // ----------------------------------------------------------------

    [Fact]
    public async Task NonNeutralPlayer_GetsRealWinRateAsWeight()
    {
        var players = TestHelpers.Players(("Veteran", false));
        var id = players[0].Id;

        // 7W / 3L = 70% WinRate, >= 3 matches → real WinRate used
        var stats = new[] { TestHelpers.Stats(id, "Veteran", wins: 7, ties: 0, losses: 3) };
        var strategy = new ManualStrategy(new FakeStatsService(stats));

        var result = await strategy.GenerateTeamsAsync(players, new TeamGenerationSettings());

        var entry = result.Options[0].Unassigned[0];
        entry.Weight.Should().BeApproximately(0.70, 1e-9,
            "non-neutral player weight = real WinRate (7/10 = 0.70)");
    }

    // ----------------------------------------------------------------
    // 6. Neutral player (< 3 matches) gets weight 0.50
    // ----------------------------------------------------------------

    [Fact]
    public async Task NeutralPlayer_GetsWeight0_50()
    {
        var players = TestHelpers.Players(("New", false));
        var id = players[0].Id;

        var stats = new[] { TestHelpers.NeutralStats(id, "New") };
        var strategy = new ManualStrategy(new FakeStatsService(stats));

        var result = await strategy.GenerateTeamsAsync(players, new TeamGenerationSettings());

        var entry = result.Options[0].Unassigned[0];
        entry.Weight.Should().BeApproximately(0.50, 1e-9,
            "neutral player (< 3 matches) must get weight 0.50");
    }

    // ----------------------------------------------------------------
    // 7. GK gets weight 0.50 when IncludeGoalkeepers = false
    //    (GK is excluded from candidatePlayers, so stats are not loaded;
    //     fallback weight is NeutralWinRate = 0.50)
    // ----------------------------------------------------------------

    [Fact]
    public async Task GK_GetsNeutralWeight_WhenIncludeGoalkeepersFalse()
    {
        var players = TestHelpers.Players(("GK", true), ("A", false));
        var ids = players.ToDictionary(p => p.Name, p => p.Id);

        // GK has real stats, but when IncludeGoalkeepers=false it's excluded from candidatePlayers
        var stats = new[]
        {
            TestHelpers.Stats(ids["GK"], "GK", wins: 10, ties: 0, losses: 0), // 100% WR
            TestHelpers.Stats(ids["A"],  "A",  wins: 5,  ties: 0, losses: 5),
        };

        var strategy = new ManualStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        var gkEntry = result.Options[0].Unassigned.First(x => x.PlayerId == ids["GK"]);

        // When IncludeGoalkeepers=false, GK is not enriched → weight falls back to NeutralWinRate = 0.50
        gkEntry.Weight.Should().BeApproximately(0.50, 1e-9,
            "GK excluded from candidatePlayers → stats not loaded → fallback weight 0.50");
    }
}
