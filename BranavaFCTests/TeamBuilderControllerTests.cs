using System.Security.Claims;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.TeamBuilder;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BranavaFC.Tests;

/// <summary>Monte seu Time é só para o admin da patota (além de Admin/GodMode da plataforma).</summary>
public sealed class TeamBuilderControllerTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static Mock<ITeamBuilderService> Service()
    {
        var mock = new Mock<ITeamBuilderService>();
        mock.Setup(s => s.GetStatsAsync(It.IsAny<Guid>(), It.IsAny<TeamBuilderRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamBuilderStatsDto>.Ok(new TeamBuilderStatsDto()));
        mock.Setup(s => s.GetPlayersAsync(It.IsAny<Guid>(), It.IsAny<TeamBuilderFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamBuilderPlayersDto>.Ok(new TeamBuilderPlayersDto()));
        return mock;
    }

    private static TeamBuilderController Sut(AppDbContext db, Mock<ITeamBuilderService> service, Guid userId, string role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            "Tests", ClaimTypes.Name, ClaimTypes.Role);
        return new TeamBuilderController(service.Object, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static async Task<(IActionResult Stats, IActionResult Players)> CallBothAsync(TeamBuilderController sut) => (
        await sut.GetStats(GroupId, new TeamBuilderRequestDto { PlayerIds = [Guid.NewGuid(), Guid.NewGuid()] }, CancellationToken.None),
        await sut.GetPlayers(GroupId, new TeamBuilderFilterDto(), CancellationToken.None));

    [Fact]
    public async Task MembroComum_RecebeForbid_ENaoCalculaNada()
    {
        await using var db = DbContextFactory.Create(nameof(MembroComum_RecebeForbid_ENaoCalculaNada));
        var service = Service();

        var (stats, players) = await CallBothAsync(Sut(db, service, Guid.NewGuid(), "User"));

        stats.Should().BeOfType<ForbidResult>();
        players.Should().BeOfType<ForbidResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminDeOutraPatota_RecebeForbid()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(AdminDeOutraPatota_RecebeForbid));
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var (stats, players) = await CallBothAsync(Sut(db, Service(), userId, "User"));

        stats.Should().BeOfType<ForbidResult>();
        players.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AdminDaPatota_RecebeOk()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(AdminDaPatota_RecebeOk));
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = GroupId });
        await db.SaveChangesAsync();

        var (stats, players) = await CallBothAsync(Sut(db, Service(), userId, "User"));

        stats.Should().BeOfType<OkObjectResult>();
        players.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("GodMode")]
    public async Task AdminDaPlataforma_RecebeOk(string role)
    {
        await using var db = DbContextFactory.Create(nameof(AdminDaPlataforma_RecebeOk) + role);

        var (stats, players) = await CallBothAsync(Sut(db, Service(), Guid.NewGuid(), role));

        stats.Should().BeOfType<OkObjectResult>();
        players.Should().BeOfType<OkObjectResult>();
    }
}
