using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;

namespace BranavaFC.Tests;

public class RandomStrategyTests
{
    private static List<PlayerRequestDto> CreatePlayers(int count)
    {
        var list = new List<PlayerRequestDto>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new PlayerRequestDto(Guid.NewGuid(), $"P{i + 1}", false));
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

        var combined = result.TeamA.Concat(result.TeamB).ToList();
        Assert.Equal(players.Count, combined.Count);
        Assert.Equal(players.Select(p => p.Id).OrderBy(id => id), combined.OrderBy(id => id));
        Assert.Equal(combined.Count, combined.Distinct().Count());
    }

    [Fact]
    public async Task RandomStrategy_Respects_Exclude_Goalkeepers()
    {
        var players = new List<PlayerRequestDto>
        {
            new PlayerRequestDto(Guid.NewGuid(), "Gk", true),
            new PlayerRequestDto(Guid.NewGuid(), "P1", false),
            new PlayerRequestDto(Guid.NewGuid(), "P2", false),
            new PlayerRequestDto(Guid.NewGuid(), "P3", false)
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var strategy = new RandomStrategy();

        var result = await strategy.GenerateTeamsAsync(players, settings);

        // GK deve ficar em Unassigned porque excluímos goleiros
        Assert.Contains(players[0].Id, result.Unassigned);

        // Os times devem ser preenchidos só com jogadores de linha (pode sobrar 1 unassigned)
        Assert.True(result.TeamA.Count + result.TeamB.Count <= 3);
    }
}
