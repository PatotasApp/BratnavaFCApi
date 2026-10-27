using System.Security.Claims;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

public sealed class MultiTeamGenerationControllerTests
{
    [Fact]
    public async Task DrawTeamColors_ReturnsOkEnvelopeWithServiceMessage()
    {
        const string warning = "Ha so 2 cor(es) ativa(s) para 3 times: 1 time(s) ficaram sem cor.";
        Guid groupId = Guid.NewGuid();
        Guid championshipId = Guid.NewGuid();
        var service = new Mock<IChampionshipService>();
        service.Setup(item => item.DrawTeamColorsAsync(groupId, championshipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(warning));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"multi-team-controller-{Guid.NewGuid()}")
            .Options);
        var controller = new ChampionshipsController(service.Object, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Role, "GodMode")], "TestAuth")),
                },
            },
        };

        IActionResult action = await controller.DrawTeamColors(groupId, championshipId, CancellationToken.None);

        var ok = action.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeAssignableTo<Result<object>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().BeNull();
        envelope.Message.Should().Be(warning);
    }
}

[Trait("Category", "Postgres")]
public sealed class MultiTeamGenerationChampionshipIntegrationTests : IClassFixture<CampeonatoPostgresFixture>
{
    private readonly CampeonatoPostgresFixture _fixture;

    public MultiTeamGenerationChampionshipIntegrationTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DrawTeams_UsesMultiTeamEngine_AndPreservesDataAndColorWarning()
    {
        await using var db = await _fixture.CreateDatabaseAsync(
            nameof(DrawTeams_UsesMultiTeamEngine_AndPreservesDataAndColorWarning));
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);
        db.TeamColors.AddRange(
            new TeamColorEntity(group.Id, "Azul", "#0000FF"),
            new TeamColorEntity(group.Id, "Verde", "#00FF00"));
        List<PlayerEntity> players = Enumerable.Range(0, 10)
            .Select(index => new PlayerEntity(
                $"Jogador {index + 1}", null, group.Id, 3m, isGoalkeeper: index < 5))
            .ToList();
        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stats = new Mock<IPlayerStatsService>();
        stats.Setup(item => item.EnrichPlayersAsync(
                It.IsAny<List<PlayerRequestDto>>(), It.IsAny<CancellationToken>()))
            .Returns((List<PlayerRequestDto> requested, CancellationToken _) =>
                Task.FromResult(Result<List<PlayerStats>>.Ok(requested.Select((player, index) => new PlayerStats
                {
                    PlayerId = player.Id,
                    Name = player.Name,
                    Wins = 3,
                    WinRate = (requested.Count - index) / (double)(requested.Count + 1),
                    SynergyWith = [],
                }).ToList())));
        var service = new ChampionshipService(db, new MultiTeamGenerationService(stats.Object));
        Guid championshipId = Ok(await service.CreateAsync(
            group.Id,
            new CreateChampionshipDto(
                "Torneio", new DateTime(2026, 12, 20, 13, 0, 0, DateTimeKind.Utc), "Arena", null),
            CancellationToken.None)).Id;
        Ok(await service.OpenAcceptationAsync(group.Id, championshipId, CancellationToken.None));
        foreach (PlayerEntity player in players)
            Ok(await service.RespondInviteAsync(group.Id, championshipId, player.Id, true, CancellationToken.None));

        Result<DefineTeamsDto> result = await service.DrawTeamsAsync(
            group.Id, championshipId, 3, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Data.Should().NotBeNull();
        result.Data!.Teams.Should().HaveCount(3);
        result.Data.Teams.SelectMany(team => team.PlayerIds).Should().HaveCount(10).And.OnlyHaveUniqueItems();
        result.Data.Teams.Select(team => team.PlayerIds.Count).Max()
            .Should().Be(result.Data.Teams.Select(team => team.PlayerIds.Count).Min() + 1);
        result.Data.Teams.Should().OnlyContain(team => team.GoalkeeperPlayerId.HasValue);
        result.Data.Teams.Select(team => team.GoalkeeperPlayerId).Should().OnlyHaveUniqueItems();
        result.Message.Should().Contain("so 2 cor(es) ativa(s) para 3 times");
        stats.Verify(item => item.EnrichPlayersAsync(
            It.IsAny<List<PlayerRequestDto>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);
}
