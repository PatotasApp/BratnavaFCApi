using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Models;
using Moq;

namespace BranavaFC.Tests;

public class AlgorithmStrategy_MoqTests
{
    [Fact]
    public async Task GenerateTeamsAsync_Calls_StatsService_And_Returns_Teams()
    {
        // Arrange
        var players = TestHelpers.Players(
            ("GK1", true), ("GK2", true),
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false), ("G", false), ("H", false), ("I", false), ("J", false)
        );

        // devolve stats básicos pros 12
        var statsList = new List<PlayerStats>();
        foreach (var p in players)
        {
            statsList.Add(new PlayerStats
            {
                PlayerId = p.Id,
                Name = p.Name,
                Wins = 5,
                Ties = 0,
                Losses = 5,
                WinRate = 0.5,
                SynergyWith = new Dictionary<Guid, double>()
            });
        }

        var stats = new Mock<IPlayerStatsService>(MockBehavior.Strict);
        stats.Setup(s => s.EnrichPlayersAsync(players, It.IsAny<CancellationToken>()))
             .ReturnsAsync(statsList);

        var strategy = new AlgorithmStrategy(stats.Object); // seu AlgorithmStrategy atual

        var settings = new TeamGenerationSettings { PlayersPerTeam = 6, IncludeGoalkeepers = true };

        // Act
        var result = await strategy.GenerateTeamsAsync(players, settings, CancellationToken.None);

        // Assert
        Assert.Equal(6, result.TeamA.Count);
        Assert.Equal(6, result.TeamB.Count);
        Assert.Empty(result.Unassigned);

        stats.Verify(s => s.EnrichPlayersAsync(players, It.IsAny<CancellationToken>()), Times.Once);
    }
}
