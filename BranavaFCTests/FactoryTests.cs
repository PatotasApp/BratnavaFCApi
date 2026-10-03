using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using Xunit;

namespace BranavaFC.Tests;

public class FactoryTests
{
    [Fact]
    public void Create_ManualStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.Manual,
            new FakeStatsService(),
            NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("ManualStrategy", s.GetType().Name);
    }

    [Fact]
    public void Create_RandomStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.Random,
            new FakeStatsService(),
            NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("RandomStrategy", s.GetType().Name);
    }

    [Fact]
    public void Create_AlgorithmStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.Algorithm,
            new FakeStatsService(),
            NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("AlgorithmStrategy", s.GetType().Name);
    }

    [Fact]
    public void Create_GroupByWinsStrategy()
    {
        var s = TeamGenerationFactory.Create(
            StrategyType.GroupByWins,
            new FakeStatsService(),
            NullLoggerFactory.Instance);

        Assert.NotNull(s);
        Assert.Equal("GroupByWinsStrategy", s.GetType().Name);
    }

    private sealed class FakeStatsService : IPlayerStatsService
    {
        // ✅ assinatura correta do seu IPlayerStatsService (SEM optionsCount)
        public Task<Result<List<PlayerStats>>> EnrichPlayersAsync(
            List<PlayerRequestDto> players,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<List<PlayerStats>>.Ok(new List<PlayerStats>()));

        public Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, bool includeGuests = true, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlayerVisualStatsReport
            {
                GroupId = groupId,
                TotalMatchesConsidered = 0,
                TotalFinalizedMatches = 0,
                TotalMatchesWithScore = 0,
                Players = new List<PlayerVisualStatsItem>()
            });

        public Task<PlayerSpotlightReport> GetSpotlightReportAsync(Guid groupId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlayerSpotlightReport { GroupId = groupId });
    }
}