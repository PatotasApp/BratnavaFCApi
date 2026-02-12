using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BratnavaFC.Tests;

public class FactoryTests
{
    [Fact]
    public void Create_ManualStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.Manual,
            statsService: new FakeStatsService(),
            loggerFactory: NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("ManualStrategy", s.GetType().Name);
    }

    [Fact]
    public void Create_RandomStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.Random,
            statsService: new FakeStatsService(),
            loggerFactory: NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("RandomStrategy", s.GetType().Name);
    }

    [Fact]
    public void Create_AlgorithmStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.Algorithm,
            statsService: new FakeStatsService(),
            loggerFactory: NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("AlgorithmStrategy", s.GetType().Name);
    }

    [Fact]
    public void Create_GroupByWinsStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.GroupByWins,
            statsService: new FakeStatsService(),
            loggerFactory: NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("GroupByWinsStrategy", s.GetType().Name);
    }

    private sealed class FakeStatsService : IPlayerStatsService
    {
        public Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PlayerStats>());

        public Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlayerVisualStatsReport
            {
                GroupId = groupId,
                TotalMatchesConsidered = 0,
                TotalFinalizedMatches = 0,
                TotalMatchesWithScore = 0,
                Players = new List<PlayerVisualStatsItem>()
            });
    }
}
