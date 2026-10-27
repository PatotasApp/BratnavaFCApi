using System.Diagnostics;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using FluentAssertions;

namespace BranavaFC.Tests;

public sealed class MultiTeamGenerationTests
{
    [Theory]
    [InlineData(3, 10)]
    [InlineData(4, 13)]
    [InlineData(5, 16)]
    public async Task Balanced_DividesAllPlayers_WithSizesDifferingByAtMostOne(
        int teamCount,
        int playerCount)
    {
        List<PlayerRequestDto> players = Players(playerCount);
        var service = ServiceWithDescendingStrength(players);

        var result = await service.GenerateAsync(
            players, teamCount, MultiTeamStrategy.Balanced, [], seed: 17);

        result.Success.Should().BeTrue(result.Error);
        result.Data!.Options.Should().NotBeEmpty();
        foreach (MultiTeamDrawOptionDto option in result.Data.Options)
        {
            option.Teams.Should().HaveCount(teamCount);
            (option.Teams.Max(team => team.PlayerIds.Count) - option.Teams.Min(team => team.PlayerIds.Count))
                .Should().BeLessThanOrEqualTo(1);
            AssertEveryPlayerExactlyOnce(option, players);
            foreach (MultiTeamDto team in option.Teams)
            {
                team.Strength.Should().BeApproximately(
                    team.PlayerIds.Sum(playerId => result.Data.PlayerStrengths[playerId]), 1e-12);
            }
            option.StrengthDifference.Should().BeApproximately(
                option.Teams.Max(team => team.Strength) - option.Teams.Min(team => team.Strength), 1e-12);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task Balanced_AssignsAtMostOneStartingGoalkeeperPerTeam_AndKeepsExtrasInLine(
        int goalkeeperCount)
    {
        const int teamCount = 4;
        List<PlayerRequestDto> players = Players(16);
        List<Guid> goalkeeperIds = players.Take(goalkeeperCount).Select(player => player.Id).ToList();
        var service = ServiceWithDescendingStrength(players);

        var result = await service.GenerateAsync(
            players, teamCount, MultiTeamStrategy.Balanced, goalkeeperIds, seed: 23);

        MultiTeamDrawOptionDto option = result.Data!.Options[0];
        List<Guid> assignedGoalkeepers = option.Teams
            .Where(team => team.GoalkeeperPlayerId.HasValue)
            .Select(team => team.GoalkeeperPlayerId!.Value)
            .ToList();

        assignedGoalkeepers.Should().HaveCount(Math.Min(goalkeeperCount, teamCount));
        assignedGoalkeepers.Should().OnlyHaveUniqueItems();
        assignedGoalkeepers.Should().OnlyContain(goalkeeperId => goalkeeperIds.Contains(goalkeeperId));
        List<Guid> extraGoalkeepers = goalkeeperIds.Skip(teamCount).ToList();
        if (extraGoalkeepers.Count > 0)
            assignedGoalkeepers.Should().NotContain(extraGoalkeepers);
        if (goalkeeperIds.Count > 0)
        {
            option.Teams.SelectMany(team => team.PlayerIds)
                .Should().Contain(goalkeeperIds, "goleiros excedentes continuam no sorteio como linha");
        }
        AssertEveryPlayerExactlyOnce(option, players);
    }

    [Fact]
    public async Task MissingStats_UsesKnownGroupAverage_NeverZero()
    {
        List<PlayerRequestDto> players = Players(4);
        PlayerStats[] stats =
        [
            RatedHistory(players[0], 0.20),
            RatedHistory(players[1], 0.60),
            RatedHistory(players[2], 0.80),
            TestHelpers.NeutralStats(players[3].Id, players[3].Name),
        ];
        var service = new MultiTeamGenerationService(new FakeStatsService(stats));

        var result = await service.GenerateAsync(
            players, 2, MultiTeamStrategy.Balanced, [], optionsCount: 1, seed: 1);

        double expectedAverage = (0.20 + 0.60 + 0.80) / 3;
        result.Data!.PlayerStrengths[players[3].Id].Should().BeApproximately(expectedAverage, 1e-12);
        result.Data.PlayerStrengths.Values.Should().OnlyContain(strength => strength > 0);
    }

    [Fact]
    public async Task MissingAllStats_UsesNeutralGroupDefault()
    {
        List<PlayerRequestDto> players = Players(9);
        var service = new MultiTeamGenerationService(new FakeStatsService([]));

        var result = await service.GenerateAsync(
            players, 3, MultiTeamStrategy.Balanced, [], optionsCount: 1, seed: 1);

        result.Data!.PlayerStrengths.Values.Should().OnlyContain(strength => Math.Abs(strength - 0.50) < 1e-12);
    }

    [Fact]
    public async Task Random_WithSameSeed_IsReproducible()
    {
        List<PlayerRequestDto> players = Players(15);
        List<Guid> goalkeepers = players.Take(5).Select(player => player.Id).ToList();
        var service = ServiceWithDescendingStrength(players);

        var first = await service.GenerateAsync(
            players, 5, MultiTeamStrategy.Random, goalkeepers, seed: 12345);
        var second = await service.GenerateAsync(
            players, 5, MultiTeamStrategy.Random, goalkeepers, seed: 12345);

        first.Data.Should().BeEquivalentTo(second.Data, options => options.WithStrictOrdering());
        first.Data!.Options.Should().HaveCount(3);
    }

    [Fact]
    public async Task Balanced_NeverWorsensItsStartingDistribution()
    {
        List<PlayerRequestDto> players = Players(19);
        var service = ServiceWithDescendingStrength(players);

        var result = await service.GenerateAsync(
            players, 5, MultiTeamStrategy.Balanced, [], optionsCount: 8, seed: 91);

        result.Data!.Options.Should().NotBeEmpty();
        result.Data.Options.Should().OnlyContain(option =>
            option.StrengthDifference <= option.BaselineStrengthDifference + 1e-12);
    }

    [Fact]
    public async Task Balanced_BestOption_NeverWorseThanSimpleSnake()
    {
        List<PlayerRequestDto> players = Players(18);
        var service = ServiceWithDescendingStrength(players);

        var result = await service.GenerateAsync(
            players, 4, MultiTeamStrategy.Balanced, [], optionsCount: 1, seed: 7);

        MultiTeamDrawOptionDto option = result.Data!.Options.Single();
        option.StrengthDifference.Should().BeLessThanOrEqualTo(
            option.BaselineStrengthDifference + 1e-12,
            "a primeira distribuição inicial é a cobra canônica ordenada por força");
    }

    [Fact]
    public async Task Balanced_ThirtyPlayersAndSixTeams_RunsBelowOneSecond()
    {
        List<PlayerRequestDto> players = Players(30);
        List<Guid> goalkeepers = players.Take(8).Select(player => player.Id).ToList();
        var service = ServiceWithDescendingStrength(players);
        var stopwatch = Stopwatch.StartNew();

        var result = await service.GenerateAsync(
            players, 6, MultiTeamStrategy.Balanced, goalkeepers, optionsCount: 5, seed: 2026);

        stopwatch.Stop();
        result.Success.Should().BeTrue(result.Error);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    private static MultiTeamGenerationService ServiceWithDescendingStrength(
        IReadOnlyList<PlayerRequestDto> players)
    {
        List<PlayerStats> stats = players.Select((player, index) =>
            RatedHistory(player, (players.Count - index) / (double)(players.Count + 1))).ToList();
        return new MultiTeamGenerationService(new FakeStatsService(stats));
    }

    private static List<PlayerRequestDto> Players(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new PlayerRequestDto(TestHelpers.GuidFromInt(index), $"Jogador {index}", false))
            .ToList();

    private static PlayerStats RatedHistory(PlayerRequestDto player, double strength) => new()
    {
        PlayerId = player.Id,
        Name = player.Name,
        Wins = 3,
        Ties = 0,
        Losses = 0,
        WinRate = strength,
        SynergyWith = [],
    };

    private static void AssertEveryPlayerExactlyOnce(
        MultiTeamDrawOptionDto option,
        IReadOnlyCollection<PlayerRequestDto> players)
    {
        List<Guid> assigned = option.Teams.SelectMany(team => team.PlayerIds).ToList();
        assigned.Should().HaveCount(players.Count);
        assigned.Should().OnlyHaveUniqueItems();
        assigned.Should().BeEquivalentTo(players.Select(player => player.Id));
    }
}
