using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Unit tests for AlgorithmStrategy.
///
/// Key rules exercised:
///  - Neutral rule: fewer than 3 matches → effective WinRate = 0.50 (NeutralWinRate)
///  - NeutralOverride: if set and player is still neutral, override replaces 0.50
///  - NeutralOverride: ignored once player has >= 3 matches (real WinRate used)
///  - GK handling: IncludeGoalkeepers=false → all GKs in Unassigned
///  - BalanceDiff = |TeamAWeight - TeamBWeight| (always consistent with weights)
///  - Options are ordered by BalanceDiff ascending
///  - No player appears in more than one bucket per option
/// </summary>
public class AlgorithmStrategyTests
{
    private static readonly TeamGenerationSettings Settings5v5 =
        new() { PlayersPerTeam = 5, IncludeGoalkeepers = false };

    private static readonly TeamGenerationSettings Settings5v5WithGK =
        new() { PlayersPerTeam = 5, IncludeGoalkeepers = true };

    // ----------------------------------------------------------------
    // 1. Empty input
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyPlayers_ReturnsEmptyOptions()
    {
        var strategy = new AlgorithmStrategy(new FakeStatsService([]));

        var result = await strategy.GenerateTeamsAsync([], Settings5v5);

        result.Options.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // 2. Structure: no overlaps, correct sizes
    // ----------------------------------------------------------------

    [Fact]
    public async Task Structure_NoOverlaps_CorrectSizes_10Players()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false), ("E", false),
            ("F", false), ("G", false), ("H", false), ("I", false), ("J", false));

        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 5, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();

        foreach (var opt in result.Options)
        {
            opt.TeamA.Should().HaveCount(5);
            opt.TeamB.Should().HaveCount(5);
            opt.Unassigned.Should().BeEmpty();

            var allIds = opt.TeamA.Select(x => x.PlayerId)
                           .Concat(opt.TeamB.Select(x => x.PlayerId))
                           .ToList();
            allIds.Should().OnlyHaveUniqueItems("no player may appear in both teams");
        }
    }

    // ----------------------------------------------------------------
    // 3. Options ordered by BalanceDiff ascending
    // ----------------------------------------------------------------

    [Fact]
    public async Task Options_OrderedByBalanceDiff_Ascending()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false), ("G", false), ("H", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = new[]
        {
            TestHelpers.Stats(ids[0], "A", wins: 10, ties: 0, losses: 0),
            TestHelpers.Stats(ids[1], "B", wins: 8,  ties: 0, losses: 2),
            TestHelpers.Stats(ids[2], "C", wins: 6,  ties: 0, losses: 4),
            TestHelpers.Stats(ids[3], "D", wins: 4,  ties: 0, losses: 6),
            TestHelpers.Stats(ids[4], "E", wins: 10, ties: 0, losses: 0),
            TestHelpers.Stats(ids[5], "F", wins: 8,  ties: 0, losses: 2),
            TestHelpers.Stats(ids[6], "G", wins: 6,  ties: 0, losses: 4),
            TestHelpers.Stats(ids[7], "H", wins: 4,  ties: 0, losses: 6),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 4, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        result.Options.Should().NotBeEmpty();

        var diffs = result.Options.Select(o => o.BalanceDiff).ToList();
        diffs.Should().BeInAscendingOrder(
            because: "options must be ordered by BalanceDiff ascending");
    }

    // ----------------------------------------------------------------
    // 4. BalanceDiff is always consistent with TeamAWeight - TeamBWeight
    // ----------------------------------------------------------------

    [Fact]
    public async Task BalanceDiff_AlwaysConsistentWithWeights()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false));

        var ids = players.Select(p => p.Id).ToList();
        var stats = ids.Select((id, i) =>
            TestHelpers.Stats(id, $"P{i}", wins: i + 3, ties: 0, losses: 3));

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        foreach (var opt in result.Options)
        {
            var expectedDiff = Math.Abs(opt.TeamAWeight - opt.TeamBWeight);
            opt.BalanceDiff.Should().BeApproximately(expectedDiff, 1e-9,
                "BalanceDiff must equal |TeamAWeight - TeamBWeight|");
        }
    }

    // ----------------------------------------------------------------
    // 5. Neutral rule: < 3 matches → weight = 0.50
    // ----------------------------------------------------------------

    [Fact]
    public async Task NeutralRule_PlayerWithFewerThan3Matches_GetsWeight0_50()
    {
        var players = TestHelpers.Players(
            ("Low", false), ("A", false), ("B", false), ("C", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            // "Low" has only 1 match total, high raw WinRate — must be neutralized to 0.50
            TestHelpers.Stats(ids[0], "Low",  wins: 1, ties: 0, losses: 0),
            TestHelpers.Stats(ids[1], "A",    wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[2], "B",    wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[3], "C",    wins: 5, ties: 0, losses: 5),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        var lowEntry = result.Options
            .SelectMany(o => o.TeamA.Concat(o.TeamB).Concat(o.Unassigned))
            .First(p => p.PlayerId == ids[0]);

        lowEntry.Weight.Should().BeApproximately(0.50, 1e-9,
            "player with < 3 matches must receive neutral weight 0.50");
    }

    // ----------------------------------------------------------------
    // 6. NeutralOverride used when player is still neutral
    // ----------------------------------------------------------------

    [Fact]
    public async Task NeutralOverride_IsUsed_WhenPlayerIsNeutral()
    {
        var players = TestHelpers.Players(
            ("Guest", false), ("A", false), ("B", false), ("C", false));

        var ids = players.Select(p => p.Id).ToList();

        // Guest has 0 matches but a 5-star rating → override = (5-1)*0.25 = 1.00
        var stats = new[]
        {
            TestHelpers.NeutralStats(ids[0], "Guest", neutralOverride: 1.00),
            TestHelpers.Stats(ids[1], "A", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[2], "B", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[3], "C", wins: 5, ties: 0, losses: 5),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        var guestEntry = result.Options
            .SelectMany(o => o.TeamA.Concat(o.TeamB).Concat(o.Unassigned))
            .First(p => p.PlayerId == ids[0]);

        guestEntry.Weight.Should().BeApproximately(1.00, 1e-9,
            "NeutralOverride=1.00 must be used as effective weight when player has < 3 matches");
    }

    // ----------------------------------------------------------------
    // 7. NeutralOverride is IGNORED when player is non-neutral
    // ----------------------------------------------------------------

    [Fact]
    public async Task NeutralOverride_IsIgnored_WhenPlayerHasEnoughMatches()
    {
        var players = TestHelpers.Players(
            ("Veteran", false), ("A", false), ("B", false), ("C", false));

        var ids = players.Select(p => p.Id).ToList();

        // Veteran has 10 matches with real WinRate 0.70, NeutralOverride = 0.25 must be ignored
        var stats = new[]
        {
            TestHelpers.Stats(ids[0], "Veteran", wins: 7, ties: 0, losses: 3, neutralOverride: 0.25),
            TestHelpers.Stats(ids[1], "A", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[2], "B", wins: 5, ties: 0, losses: 5),
            TestHelpers.Stats(ids[3], "C", wins: 5, ties: 0, losses: 5),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        var veteranEntry = result.Options
            .SelectMany(o => o.TeamA.Concat(o.TeamB).Concat(o.Unassigned))
            .First(p => p.PlayerId == ids[0]);

        veteranEntry.Weight.Should().BeApproximately(0.70, 1e-9,
            "real WinRate must be used; NeutralOverride is ignored when player has >= 3 matches");
    }

    // ----------------------------------------------------------------
    // 8. GK exclusion: IncludeGoalkeepers = false
    // ----------------------------------------------------------------

    [Fact]
    public async Task GoalkeeperExclusion_GKsGoToUnassigned_WhenSettingFalse()
    {
        var players = TestHelpers.Players(
            ("GK1", true), ("GK2", true),
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.ToDictionary(p => p.Name, p => p.Id);
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();

        foreach (var opt in result.Options)
        {
            var teamIds = opt.TeamA.Select(x => x.PlayerId)
                            .Concat(opt.TeamB.Select(x => x.PlayerId))
                            .ToHashSet();

            teamIds.Should().NotContain(ids["GK1"], "GK must not be assigned to a team");
            teamIds.Should().NotContain(ids["GK2"], "GK must not be assigned to a team");

            var unassignedIds = opt.Unassigned.Select(x => x.PlayerId).ToHashSet();
            unassignedIds.Should().Contain(ids["GK1"], "GK1 must be in Unassigned");
            unassignedIds.Should().Contain(ids["GK2"], "GK2 must be in Unassigned");
        }
    }

    // ----------------------------------------------------------------
    // 9. Perfect balance: all players neutral → BalanceDiff ~ 0
    // ----------------------------------------------------------------

    [Fact]
    public async Task PerfectBalance_AllNeutralPlayers_FirstOption_BalanceDiffIsZero()
    {
        // 4 players all neutral (< 3 matches) → all get weight 0.50
        // 2v2 must yield BalanceDiff = |1.00 - 1.00| = 0
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var stats = players.Select(p => TestHelpers.NeutralStats(p.Id, p.Name));

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        result.Options.First().BalanceDiff.Should().BeApproximately(0.0, 1e-9,
            "all neutral players have equal weight 0.50, so any 2v2 split is perfectly balanced");
    }

    // ----------------------------------------------------------------
    // 10. Dimensional diffs expostos quando ratings estão presentes
    // ----------------------------------------------------------------

    [Fact]
    public async Task DimensionalDiffs_Populated_WhenRatingsSet()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedStats(ids[0], "A", wins:5, ties:0, losses:5, attackRating:0.8, defenseRating:0.2, physicalRating:0.6),
            TestHelpers.RatedStats(ids[1], "B", wins:5, ties:0, losses:5, attackRating:0.7, defenseRating:0.3, physicalRating:0.5),
            TestHelpers.RatedStats(ids[2], "C", wins:5, ties:0, losses:5, attackRating:0.2, defenseRating:0.8, physicalRating:0.6),
            TestHelpers.RatedStats(ids[3], "D", wins:5, ties:0, losses:5, attackRating:0.3, defenseRating:0.7, physicalRating:0.5),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();
        var opt = result.Options.First();

        opt.AttackDiff.Should().HaveValue("AttackDiff deve ser populado quando ratings estão presentes");
        opt.DefenseDiff.Should().HaveValue("DefenseDiff deve ser populado quando ratings estão presentes");
        opt.PhysicalDiff.Should().HaveValue("PhysicalDiff deve ser populado quando ratings estão presentes");
    }

    // ----------------------------------------------------------------
    // 11. Explicação inclui info dimensional quando ratings existem
    // ----------------------------------------------------------------

    [Fact]
    public async Task Explanation_ContainsDimensionalInfo_WhenRatingsSet()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedStats(ids[0], "A", wins:5, ties:0, losses:5, attackRating:0.9, defenseRating:0.1, physicalRating:0.6),
            TestHelpers.RatedStats(ids[1], "B", wins:5, ties:0, losses:5, attackRating:0.1, defenseRating:0.9, physicalRating:0.5),
            TestHelpers.RatedStats(ids[2], "C", wins:5, ties:0, losses:5, attackRating:0.8, defenseRating:0.2, physicalRating:0.6),
            TestHelpers.RatedStats(ids[3], "D", wins:5, ties:0, losses:5, attackRating:0.2, defenseRating:0.8, physicalRating:0.5),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();
        var opt = result.Options.First();

        opt.Explanation.Should().NotBeNull();
        opt.Explanation!.Conclusao.Should().Contain("Dimensões",
            "a conclusão deve incluir o breakdown dimensional quando ratings estão definidos");
        opt.Explanation.Resumo.Should().NotBeEmpty();
    }

    // ----------------------------------------------------------------
    // 12. Janela de tolerância (0.05): opção com melhor equilíbrio
    //     dimensional vence dentro da janela, mesmo com BalanceDiff
    //     levemente maior
    // ----------------------------------------------------------------

    [Fact]
    public async Task ToleranceWindow_BetterDimensionalBalance_RanksAhead_WithinWindow()
    {
        // Setup: 4 jogadores, 2v2
        //   A (W=0.76, Ofensivo: atk=0.9)
        //   B (W=0.74, Ofensivo: atk=0.9)
        //   C (W=0.76, Defensivo: atk=0.1)
        //   D (W=0.74, Defensivo: atk=0.1)
        //
        // 3 opções de 2v2:
        //   {A,B} vs {C,D}: BalanceDiff=0.00, AttackDiff=1.6 (péssimo dimensional)
        //   {A,C} vs {B,D}: BalanceDiff=0.04, AttackDiff=0.0 (ótimo dimensional, dentro da janela 0.05)
        //   {A,D} vs {B,C}: BalanceDiff=0.00, AttackDiff=0.0 (ótimo em tudo → sempre 1o)
        //
        // Com tolerância 0.05: as 3 opções entram na janela e são ordenadas por AttackDiff.
        // O resultado: {A,D/B,C} (diff=0, atk=0) em 1o, {A,C/B,D} (diff=0.04, atk=0) em 2o,
        // {A,B/C,D} (diff=0.00, atk=1.6) em 3o.
        // Ou seja, {A,B} vs {C,D} (pior dimensional) NÃO pode ser a 2a opção.

        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedStats(ids[0], "A", wins:76, ties:0, losses:24, attackRating:0.9, defenseRating:0.1, physicalRating:0.5),
            TestHelpers.RatedStats(ids[1], "B", wins:74, ties:0, losses:26, attackRating:0.9, defenseRating:0.1, physicalRating:0.5),
            TestHelpers.RatedStats(ids[2], "C", wins:76, ties:0, losses:24, attackRating:0.1, defenseRating:0.9, physicalRating:0.5),
            TestHelpers.RatedStats(ids[3], "D", wins:74, ties:0, losses:26, attackRating:0.1, defenseRating:0.9, physicalRating:0.5),
        };

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 3);

        result.Options.Should().HaveCount(3, "existem exatamente 3 composições 2v2 únicas com 4 jogadores");

        // A última opção deve ser a que tem ambos os ofensivos juntos (pior dimensional)
        var last = result.Options.Last();
        var lastTeamAIds = last.TeamA.Select(x => x.PlayerId).ToHashSet();
        var lastTeamBIds = last.TeamB.Select(x => x.PlayerId).ToHashSet();

        bool lastHasBothOffensivesTogether =
            (lastTeamAIds.Contains(ids[0]) && lastTeamAIds.Contains(ids[1])) ||
            (lastTeamBIds.Contains(ids[0]) && lastTeamBIds.Contains(ids[1]));

        lastHasBothOffensivesTogether.Should().BeTrue(
            "a opção com ambos os ofensivos no mesmo time (pior dimensional) deve ser a última " +
            "dentro da janela de tolerância de 0.05");
    }
}
