using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;
using Xunit;

namespace BratnavaFC.Tests;

public class RandomStrategyTests
{
    private static List<Player> CreatePlayers(int count)
    {
        var list = new List<Player>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new Player { Id = Guid.NewGuid(), Name = $"P{i + 1}", IsGoalkeeper = false });
        }
        return list;
    }

    [Fact]
    public async Task RandomStrategy_Distributes_Correct_Number_And_No_Duplicates()
    {
        var players = CreatePlayers(6);
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = true };
        var strategy = new RandomStrategy();

        var result = await strategy.GenerateTeamsAsync(players, settings);

        Assert.Equal(3, result.TeamA.Count);
        Assert.Equal(3, result.TeamB.Count);
        Assert.Empty(result.Unassigned);

        // union equals input set
        var combined = result.TeamA.Concat(result.TeamB).ToList();
        Assert.Equal(players.Count, combined.Count);
        Assert.Equal(players.Select(p => p.Id).OrderBy(id => id), combined.OrderBy(id => id));
        // no duplicates
        Assert.Equal(combined.Count, combined.Distinct().Count());
    }

    [Fact]
    public async Task RandomStrategy_Respects_Exclude_Goalkeepers()
    {
        var players = new List<Player>
        {
            new Player { Id = Guid.NewGuid(), Name = "Gk", IsGoalkeeper = true },
            new Player { Id = Guid.NewGuid(), Name = "P1", IsGoalkeeper = false },
            new Player { Id = Guid.NewGuid(), Name = "P2", IsGoalkeeper = false },
            new Player { Id = Guid.NewGuid(), Name = "P3", IsGoalkeeper = false }
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var strategy = new RandomStrategy();

        var result = await strategy.GenerateTeamsAsync(players, settings);

        // GK should be unassigned because we excluded goalkeepers
        Assert.Contains(players[0].Id, result.Unassigned);
        // The two teams should be filled from the three non-GK players (may leave one unassigned)
        Assert.True(result.TeamA.Count + result.TeamB.Count <= 3);
    }
}