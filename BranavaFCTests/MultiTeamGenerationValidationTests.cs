using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Dtos;
using FluentAssertions;

namespace BranavaFC.Tests;

public sealed class MultiTeamGenerationValidationTests
{
    [Fact]
    public async Task RejectsFewerPlayersThanTeams()
    {
        List<PlayerRequestDto> players = Players(2);
        var service = new MultiTeamGenerationService(new FakeStatsService([]));

        var result = await service.GenerateAsync(players, 3, MultiTeamStrategy.Balanced, []);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Há menos jogadores confirmados do que times.");
    }

    [Fact]
    public async Task RejectsDuplicatedPlayers()
    {
        List<PlayerRequestDto> players = Players(3);
        players.Add(players[0]);
        var service = new MultiTeamGenerationService(new FakeStatsService([]));

        var result = await service.GenerateAsync(players, 2, MultiTeamStrategy.Random, []);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("A lista de jogadores contém IDs repetidos.");
    }

    [Fact]
    public async Task RejectsGoalkeeperWhoIsNotConfirmed()
    {
        List<PlayerRequestDto> players = Players(6);
        var service = new MultiTeamGenerationService(new FakeStatsService([]));

        var result = await service.GenerateAsync(
            players, 3, MultiTeamStrategy.Balanced, [Guid.NewGuid()]);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("A lista de goleiros contém jogador não confirmado.");
    }

    private static List<PlayerRequestDto> Players(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new PlayerRequestDto(TestHelpers.GuidFromInt(index), $"Jogador {index}", false))
            .ToList();
}
