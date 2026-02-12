using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Models;
using Moq;

namespace BranavaFC.Tests;

public class GroupByWinsStrategy_MoqTests
{
    [Fact]
    public async Task GenerateTeamsAsync_Calls_StatsService_And_Uses_Wins_Order()
    {
        // Arrange
        var players = TestHelpers.Players(("P1", false), ("P2", false), ("P3", false), ("P4", false));
        var ids = players.ToDictionary(p => p.Name, p => p.Id);

        var statsList = new List<PlayerStats>
        {
            new() { PlayerId = ids["P1"], Name="P1", Wins=10, WinRate=0.7, SynergyWith=new() },
            new() { PlayerId = ids["P2"], Name="P2", Wins=8,  WinRate=0.6, SynergyWith=new() },
            new() { PlayerId = ids["P3"], Name="P3", Wins=6,  WinRate=0.5, SynergyWith=new() },
            new() { PlayerId = ids["P4"], Name="P4", Wins=4,  WinRate=0.4, SynergyWith=new() },
        };

        var stats = new Mock<IPlayerStatsService>(MockBehavior.Strict);
        stats.Setup(s => s.EnrichPlayersAsync(players, It.IsAny<CancellationToken>()))
             .ReturnsAsync(statsList);

        var strategy = new GroupByWinsStrategy(stats.Object);
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = true };

        // Act
        var result = await strategy.GenerateTeamsAsync(players, settings, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.TeamA.Count);
        Assert.Equal(2, result.TeamB.Count);

        // Verifica que chamou o stats service
        stats.Verify(s => s.EnrichPlayersAsync(players, It.IsAny<CancellationToken>()), Times.Once);
    }
}
