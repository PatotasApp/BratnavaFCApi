using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

public class TeamGenerationServiceTests
{
    //[Fact]
    //public async Task GenerateAsync_Request_Null_Throws()
    //{
    //    var stats = new Mock<IPlayerStatsService>(MockBehavior.Strict);
    //    var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

    //    await Assert.ThrowsAsync<ArgumentNullException>(() => svc.GenerateAsync(null!, 3, CancellationToken.None));
    //}

    //[Fact]
    //public async Task GenerateAsync_Players_Null_Throws()
    //{
    //    var stats = new Mock<IPlayerStatsService>(MockBehavior.Strict);
    //    var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

    //    var req = new TeamGenerationRequestDto(
    //        Players: null!,
    //        StrategyType: StrategyType.Random,
    //        PlayersPerTeam: 5,
    //        IncludeGoalkeepers: true
    //    );

    //    await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GenerateAsync(req, 3, CancellationToken.None));
    //}

    //[Fact]
    //public async Task GenerateAsync_RandomStrategy_Returns_3Options_With_Valid_Teams()
    //{
    //    var stats = new Mock<IPlayerStatsService>(MockBehavior.Loose);
    //    var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

    //    var players = TestHelpers.Players(("A", false), ("B", false), ("C", false), ("D", false));

    //    var req = new TeamGenerationRequestDto(
    //        Players: players,
    //        StrategyType: StrategyType.Random,
    //        PlayersPerTeam: 2,
    //        IncludeGoalkeepers: true
    //    );

    //    var result = await svc.GenerateAsync(req, 3, CancellationToken.None);

    //    Assert.NotNull(result);
    //    Assert.NotNull(result.Options);
    //    Assert.Equal(3, result.Options.Count);

    //    foreach (var opt in result.Options)
    //    {
    //        Assert.Equal(2, opt.TeamA.Count);
    //        Assert.Equal(2, opt.TeamB.Count);

    //        var aIds = opt.TeamA.Select(x => x.PlayerId).ToList();
    //        var bIds = opt.TeamB.Select(x => x.PlayerId).ToList();

    //        // sem duplicar e sem interseção
    //        Assert.Equal(aIds.Count, aIds.Distinct().Count());
    //        Assert.Equal(bIds.Count, bIds.Distinct().Count());
    //        Assert.Empty(aIds.Intersect(bIds));

    //        // usou 4 players
    //        Assert.Equal(4, aIds.Count + bIds.Count);

    //        // ninguém sobrando
    //        Assert.True(opt.Unassigned == null || opt.Unassigned.Count == 0);
    //    }
    //}

    //[Fact]
    //public async Task GenerateAsync_ManualStrategy_All_Unassigned_In_All_Options()
    //{
    //    var stats = new Mock<IPlayerStatsService>(MockBehavior.Loose);
    //    var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

    //    var players = TestHelpers.Players(("A", false), ("B", false), ("C", true));

    //    var req = new TeamGenerationRequestDto(
    //        Players: players,
    //        StrategyType: StrategyType.Manual,
    //        PlayersPerTeam: 2,
    //        IncludeGoalkeepers: true
    //    );

    //    var result = await svc.GenerateAsync(req, 3, CancellationToken.None);

    //    Assert.NotNull(result);
    //    Assert.NotNull(result.Options);
    //    Assert.Equal(3, result.Options.Count);

    //    foreach (var opt in result.Options)
    //    {
    //        Assert.Empty(opt.TeamA);
    //        Assert.Empty(opt.TeamB);

    //        Assert.NotNull(opt.Unassigned);
    //        Assert.Equal(players.Count, opt.Unassigned.Count);

    //        // garante que estão todos lá
    //        var expected = players.Select(p => p.Id).OrderBy(x => x).ToList();
    //        var actual = opt.Unassigned.OrderBy(x => x).ToList();
    //    }
    //}
}