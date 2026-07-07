using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BranavaFC.Tests;

public class TeamGenerationServiceTests
{
    private static TeamGenerationService Sut(IEnumerable<PlayerStats>? stats = null)
        => new(new FakeStatsService(stats ?? []), NullLoggerFactory.Instance);

    // ─── Constructor guards ──────────────────────────────────────────────────

    [Fact]
    public void Ctor_WithNullStatsService_Throws()
    {
        var act = () => new TeamGenerationService(null!, NullLoggerFactory.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_WithNullLoggerFactory_Throws()
    {
        var act = () => new TeamGenerationService(new FakeStatsService([]), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ─── GenerateAsync(list) overload ────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_WithNullPlayersList_Throws()
    {
        var sut = Sut();

        var act = async () => await sut.GenerateAsync(
            null!, StrategyType.Random, playersPerTeam: 5, includeGoalkeepers: false);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GenerateAsync_WithValidPlayers_ReturnsOkWithOptions()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false),
            ("E", false), ("F", false));
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 5, ties: 0, losses: 5));
        var sut = Sut(stats);

        var result = await sut.GenerateAsync(
            players, StrategyType.Random, playersPerTeam: 3, includeGoalkeepers: false, optionsCount: 2);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Options.Should().NotBeEmpty();
        result.Data.Options.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task GenerateAsync_WithEmptyPlayers_ReturnsOkWithNoOptions()
    {
        var sut = Sut();

        var result = await sut.GenerateAsync(
            [], StrategyType.Random, playersPerTeam: 5, includeGoalkeepers: true);

        result.Success.Should().BeTrue();
        result.Data!.Options.Should().BeEmpty();
    }

    // ─── GenerateAsync(request) overload ─────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_WithNullRequest_ReturnsBadRequest()
    {
        var sut = Sut();

        var result = await sut.GenerateAsync((TeamGenerationRequestDto)null!);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Be("Request inválido.");
    }

    [Fact]
    public async Task GenerateAsync_WithNullPlayersInRequest_ReturnsBadRequest()
    {
        var sut = Sut();
        var request = new TeamGenerationRequestDto(null!, StrategyType.Random, 5, false);

        var result = await sut.GenerateAsync(request);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Be("Players é obrigatório.");
    }

    [Fact]
    public async Task GenerateAsync_WithValidRequest_DelegatesToStrategy()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));
        var stats = players.Select(p => TestHelpers.Stats(p.Id, p.Name, wins: 3, ties: 1, losses: 2));
        var sut = Sut(stats);

        var request = new TeamGenerationRequestDto(players, StrategyType.Random, 2, false);

        var result = await sut.GenerateAsync(request, optionsCount: 1);

        result.Success.Should().BeTrue();
        result.Data!.Options.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_WithUnknownStrategy_Throws()
    {
        var sut = Sut();
        var players = TestHelpers.Players(("A", false), ("B", false));

        var act = async () => await sut.GenerateAsync(
            players, (StrategyType)999, playersPerTeam: 1, includeGoalkeepers: false);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
