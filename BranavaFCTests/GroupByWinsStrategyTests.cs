using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BranavaFC.Tests;

public class GroupByWinsStrategyTests
{
    [Fact]
    public async Task GroupByWins_Distributes_As_Expected()
    {
        var players = new List<PlayerRequestDto>
        {
            new PlayerRequestDto(Guid.NewGuid(), "P1", false),
            new PlayerRequestDto(Guid.NewGuid(), "P2", false),
            new PlayerRequestDto(Guid.NewGuid(), "P3", false),
            new PlayerRequestDto(Guid.NewGuid(), "P4", false),
        };

        var p1 = players[0];
        var p2 = players[1];
        var p3 = players[2];
        var p4 = players[3];

        // wins ordenados: P1(10), P2(8), P3(6), P4(4)
        var fakeStatsService = new FakeStatsService(new Dictionary<Guid, int>
        {
            { p1.Id, 10 },
            { p2.Id, 8 },
            { p3.Id, 6 },
            { p4.Id, 4 }
        });

        var strategy = new GroupByWinsStrategy(fakeStatsService);
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = true };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        Assert.Equal(2, result.TeamA.Count);
        Assert.Equal(2, result.TeamB.Count);

        // regra do seed: entre (P1, P2) -> menor wins vai pro A (P2), maior vai pro B (P1)
        // depois alterna: P3 -> A, P4 -> B
        Assert.Contains(p2.Id, result.TeamA);
        Assert.Contains(p3.Id, result.TeamA);

        Assert.Contains(p1.Id, result.TeamB);
        Assert.Contains(p4.Id, result.TeamB);
    }

    private sealed class FakeStatsService : IPlayerStatsService
    {
        private readonly Dictionary<Guid, int> _wins;

        public FakeStatsService(Dictionary<Guid, int> wins)
        {
            _wins = wins ?? new Dictionary<Guid, int>();
        }

        public Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default)
        {
            var list = players.Select(p => new PlayerStats
            {
                PlayerId = p.Id,
                Name = p.Name,
                Wins = _wins.GetValueOrDefault(p.Id),
                Ties = 0,
                Losses = 0,
                WinRate = 0.0,
                SynergyWith = new Dictionary<Guid, double>()
            }).ToList();

            return Task.FromResult(list);
        }

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
