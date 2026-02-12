using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests;

public class TeamGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_Request_Null_Throws()
    {
        // Arrange
        var stats = new Mock<IPlayerStatsService>(MockBehavior.Strict);
        var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

        // Act + Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.GenerateAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_Players_Null_Throws()
    {
        // Arrange
        var stats = new Mock<IPlayerStatsService>(MockBehavior.Strict);
        var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

        // record posicional -> passa Players=null
        var req = new TeamGenerationRequestDto(
            Players: null!,
            StrategyType: StrategyType.Random,
            PlayersPerTeam: 5,
            IncludeGoalkeepers: true
        );

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GenerateAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_RandomStrategy_Returns_Valid_Teams()
    {
        // Arrange
        var stats = new Mock<IPlayerStatsService>(MockBehavior.Loose); // Random nao precisa de stats
        var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

        var players = TestHelpers.Players(("A", false), ("B", false), ("C", false), ("D", false));

        var req = new TeamGenerationRequestDto(
            Players: players,
            StrategyType: StrategyType.Random,
            PlayersPerTeam: 2,
            IncludeGoalkeepers: true
        );

        // Act
        var result = await svc.GenerateAsync(req, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.TeamA.Count);
        Assert.Equal(2, result.TeamB.Count);
        Assert.Empty(result.Unassigned);
        Assert.Equal(4, result.TeamA.Count + result.TeamB.Count);
    }

    [Fact]
    public async Task GenerateAsync_ManualStrategy_All_Unassigned()
    {
        // Arrange
        var stats = new Mock<IPlayerStatsService>(MockBehavior.Loose);
        var svc = new TeamGenerationService(stats.Object, NullLoggerFactory.Instance);

        var players = TestHelpers.Players(("A", false), ("B", false), ("C", true));

        var req = new TeamGenerationRequestDto(
            Players: players,
            StrategyType: StrategyType.Manual,
            PlayersPerTeam: 2,
            IncludeGoalkeepers: true
        );

        // Act
        var result = await svc.GenerateAsync(req, CancellationToken.None);

        // Assert
        Assert.Empty(result.TeamA);
        Assert.Empty(result.TeamB);
        Assert.Equal(players.Count, result.Unassigned.Count);
    }
}
