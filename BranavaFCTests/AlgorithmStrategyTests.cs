using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;
using Xunit;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Tests;

public class AlgorithmStrategyTests
{
    [Fact]
    public async Task AlgorithmStrategy_SeedsByWinRate_And_Distributes()
    {
        // Arrange - four players
        var p1 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P1", IsGoalkeeper = false };
        var p2 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P2", IsGoalkeeper = false };
        var p3 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P3", IsGoalkeeper = false };
        var p4 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P4", IsGoalkeeper = false };

        var players = new List<PlayerEntity> { p1, p2, p3, p4 };

        // Fake stats: winrates to determine keys and subsequent ordering
        var stats = new Dictionary<Guid, PlayerStats>
        {
            [p1.Id] = new PlayerStats { PlayerId = p1.Id, Wins = 9, WinRate = 0.90, SynergyWith = new() },
            [p2.Id] = new PlayerStats { PlayerId = p2.Id, Wins = 8, WinRate = 0.80, SynergyWith = new() },
            [p3.Id] = new PlayerStats { PlayerId = p3.Id, Wins = 4, WinRate = 0.40, SynergyWith = new() },
            [p4.Id] = new PlayerStats { PlayerId = p4.Id, Wins = 3, WinRate = 0.30, SynergyWith = new() }
        };

        var fakeStats = new FakeStatsService(stats);
        var strategy = new AlgorithmStrategy(fakeStats);
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = true };

        // Act
        var result = await strategy.GenerateTeamsAsync(players, settings);

        // Assert
        // Expected behavior:
        // - keys: p1 (0.9) and p2 (0.8) => player with smaller winrate among the two starts TeamA => p2 -> TeamA, p1 -> TeamB
        // - remaining picks: p3 then p4 (based on winrate), so TeamA: p2,p3 ; TeamB: p1,p4
        Assert.Equal(2, result.TeamA.Count);
        Assert.Equal(2, result.TeamB.Count);

        Assert.Contains(p2.Id, result.TeamA);
        Assert.Contains(p3.Id, result.TeamA);

        Assert.Contains(p1.Id, result.TeamB);
        Assert.Contains(p4.Id, result.TeamB);
    }

    [Fact]
    public async Task AlgorithmStrategy_Respects_Synergy_When_Selecting()
    {
        // Arrange - six players, strong synergy between p1 and p3 should favor p3 joining p1
        var p1 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P1", IsGoalkeeper = false };
        var p2 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P2", IsGoalkeeper = false };
        var p3 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P3", IsGoalkeeper = false };
        var p4 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P4", IsGoalkeeper = false };
        var p5 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P5", IsGoalkeeper = false };
        var p6 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P6", IsGoalkeeper = false };

        var players = new List<PlayerEntity> { p1, p2, p3, p4, p5, p6 };

        // Stats: p1 and p2 are keys (higher winrates)
        // p3 has much higher synergy with p1 than p4 has, so when it's p1's team's turn, p3 should be chosen before p4 despite winrate
        var statsDict = new Dictionary<Guid, PlayerStats>
        {
            [p1.Id] = new PlayerStats { PlayerId = p1.Id, Wins = 10, WinRate = 0.9, SynergyWith = new Dictionary<Guid, double> { [p3.Id] = 0.95 } },
            [p2.Id] = new PlayerStats { PlayerId = p2.Id, Wins = 9, WinRate = 0.85, SynergyWith = new Dictionary<Guid, double>() },

            [p3.Id] = new PlayerStats { PlayerId = p3.Id, Wins = 2, WinRate = 0.2, SynergyWith = new Dictionary<Guid, double> { [p1.Id] = 0.9 } },
            [p4.Id] = new PlayerStats { PlayerId = p4.Id, Wins = 4, WinRate = 0.4, SynergyWith = new Dictionary<Guid, double>() },

            [p5.Id] = new PlayerStats { PlayerId = p5.Id, Wins = 3, WinRate = 0.3, SynergyWith = new Dictionary<Guid, double>() },
            [p6.Id] = new PlayerStats { PlayerId = p6.Id, Wins = 1, WinRate = 0.1, SynergyWith = new Dictionary<Guid, double>() }
        };

        var fakeStats = new FakeStatsService(statsDict);
        var strategy = new AlgorithmStrategy(fakeStats);
        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = true };

        // Act
        var result = await strategy.GenerateTeamsAsync(players, settings);

        // Assert
        // Seed: keys p1 (0.9) and p2 (0.85) -> smaller of two becomes TeamA start => p2 -> TeamA, p1 -> TeamB
        // Picks alternate: TeamA picks first (p2 present), TeamB picks then...
        // We want to ensure that p3 (high synergy with p1) ends in same team as p1 (TeamB).
        Assert.Equal(3, result.TeamA.Count);
        Assert.Equal(3, result.TeamB.Count);

        // p1 should be in TeamB
        Assert.Contains(p1.Id, result.TeamB);

        // p3 should be in same team as p1 due to strong synergy
        Assert.Contains(p3.Id, result.TeamB);
    }

    private sealed class FakeStatsService : IPlayerStatsService
    {
        private readonly Dictionary<Guid, PlayerStats> _stats;

        public FakeStatsService(Dictionary<Guid, PlayerStats> stats)
        {
            _stats = stats ?? new Dictionary<Guid, PlayerStats>();
        }

        public Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerEntity> players)
        {
            var list = players.Select(p =>
            {
                if (_stats.TryGetValue(p.Id, out var s))
                    return s;
                return new PlayerStats { PlayerId = p.Id, Wins = 0, Ties = 0, Losses = 0, WinRate = 0.0, SynergyWith = new() };
            }).ToList();

            return Task.FromResult(list);
        }
    }
}