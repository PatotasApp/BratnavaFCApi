using System.Security.Claims;
using System.Text.Json;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BranavaFC.Tests;

public sealed class GroupRolesControllerTests
{
    [Fact]
    public async Task GetMyRolesAsync_GodModeWithoutGroupLinks_ReturnsNoGroupRoles()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(GetMyRolesAsync_GodModeWithoutGroupLinks_ReturnsNoGroupRoles));
        var sut = CreateSut(db, userId, "GodMode");

        var result = await sut.GetMyRolesAsync(groupId, CancellationToken.None);

        ReadRoles(result).Should().Be((false, false));
    }

    [Fact]
    public async Task GetMyRolesAsync_ReturnsOnlyExplicitRolesFromRequestedGroup()
    {
        var userId = Guid.NewGuid();
        var requestedGroupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(GetMyRolesAsync_ReturnsOnlyExplicitRolesFromRequestedGroup));
        db.GroupAdmins.Add(new GroupAdminEntity
        {
            UserId = userId,
            GroupId = otherGroupId,
        });
        db.GroupFinanceiros.Add(new GroupFinanceiroEntity
        {
            UserId = userId,
            GroupId = requestedGroupId,
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, userId, "GodMode");

        var result = await sut.GetMyRolesAsync(requestedGroupId, CancellationToken.None);

        ReadRoles(result).Should().Be((false, true));
    }

    private static GroupsController CreateSut(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid userId,
        string role)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
            ],
            "Tests",
            ClaimTypes.Name,
            ClaimTypes.Role);
        return new GroupsController(Mock.Of<IGroupService>(), db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                },
            },
        };
    }

    private static (bool IsAdmin, bool IsFinanceiro) ReadRoles(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var data = json.RootElement.GetProperty("data");
        return (
            data.GetProperty("isAdmin").GetBoolean(),
            data.GetProperty("isFinanceiro").GetBoolean());
    }
}
