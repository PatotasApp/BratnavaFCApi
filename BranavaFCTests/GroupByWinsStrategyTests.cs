using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using Xunit;

namespace BratnavaFC.Tests;

public class GroupByWinsStrategyTests
{
    [Fact]
    public async Task GroupByWins_Distributes_As_Expected()
    {
        // prepare players with Wins values
        var p1 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P1", IsGoalkeeper = false };
        var p2 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P2", IsGoalkeeper = false };
        var p3 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P3", IsGoalkeeper = false };
        var p4 = new PlayerEntity { Id = Guid.NewGuid(), Name = "P4", IsGoalkeeper = false };

        var players = new List<PlayerEntity> { p1, p2, p3, p4 };

        // create fake stats service that returns wins matching intended ordering
        var fakeStatsService = new FakeStatsService(new Dictionary<Guid, int>
        {
            { p1.Id, 10 }, // most wins
            { p2.Id, 8 },
            { p3.Id, 6 },
            { p4.Id, 4 }
        });

        var strategy = new GroupByWinsStrategy(fakeStatsService);
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = true };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        // According to algorithm:
        // ordered by wins desc: [p1(10), p2(8), p3(6), p4(4)]
        // first two: between p1 and p2, player with fewer wins (p2) -> TeamA, p1 -> TeamB
        // then p3 -> TeamA, p4 -> TeamB
        Assert.Equal(2, result.TeamA.Count);
        Assert.Equal(2, result.TeamB.Count);

        Assert.Contains(p2.Id, result.TeamA);
        Assert.Contains(p3.Id, result.TeamA);

        Assert.Contains(p1.Id, result.TeamB);
        Assert.Contains(p4.Id, result.TeamB);
    }

    // minimal fake stats service for tests
    private sealed class FakeStatsService : IPlayerStatsService
    {
        private readonly Dictionary<Guid, int> _wins;

        public FakeStatsService(Dictionary<Guid, int> wins)
        {
            _wins = wins;
        }

        public Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerEntity> players, CancellationToken cancellationToken = default)
        {
            var list = players.Select(p => new PlayerStats
            {
                PlayerId = p.Id,
                Wins = _wins.GetValueOrDefault(p.Id),
                Ties = 0,
                Losses = 0,
                WinRate = 0.0,
                SynergyWith = new Dictionary<Guid, double>()
            }).ToList();

            return Task.FromResult(list);
        }

        // Novo método da interface -> no teste não usamos, então pode retornar vazio.
        public Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PlayerVisualStatsReport
            {
                GroupId = groupId,
                TotalMatchesConsidered = 0,
                TotalFinalizedMatches = 0,
                TotalMatchesWithScore = 0,
                Players = new List<PlayerVisualStatsItem>()
            });
        }
    }
}
