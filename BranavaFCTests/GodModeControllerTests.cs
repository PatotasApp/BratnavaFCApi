using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BranavaFC.Tests;

public sealed class GodModeControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GodModeController CreateSut(
        Mock<IPushService>? push = null,
        BratnavaFC.Infrastructure.Data.AppDbContext? db = null,
        string dbName = "GodMode_Default")
    {
        push ??= new Mock<IPushService>();
        db   ??= DbContextFactory.Create(dbName);
        return new GodModeController(push.Object, db);
    }

    private static UserEntity MakeActiveUser(string suffix)
        => new($"user_{suffix}", "A", "B", $"{suffix}@test.com", "hash", null, null);

    private static UserEntity MakeInactiveUser(string suffix)
    {
        var u = new UserEntity($"inactive_{suffix}", "X", "Y", $"inactive_{suffix}@test.com", "hash", null, null);
        u.Inactivate();
        return u;
    }

    // ── NotifyGroupAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyGroupAsync_WhenTitleIsEmpty_ShouldReturnBadRequest()
    {
        var sut = CreateSut(dbName: nameof(NotifyGroupAsync_WhenTitleIsEmpty_ShouldReturnBadRequest));

        var result = await sut.NotifyGroupAsync(
            Guid.NewGuid(),
            new GodModeController.NotifyRequest("", "Body"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task NotifyGroupAsync_WhenBodyIsEmpty_ShouldReturnBadRequest()
    {
        var sut = CreateSut(dbName: nameof(NotifyGroupAsync_WhenBodyIsEmpty_ShouldReturnBadRequest));

        var result = await sut.NotifyGroupAsync(
            Guid.NewGuid(),
            new GodModeController.NotifyRequest("Title", "   "),
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task NotifyGroupAsync_WhenValid_ShouldCallSendToGroupAsync()
    {
        var push    = new Mock<IPushService>();
        var groupId = Guid.NewGuid();
        var sut     = CreateSut(push, dbName: nameof(NotifyGroupAsync_WhenValid_ShouldCallSendToGroupAsync));

        var result = await sut.NotifyGroupAsync(
            groupId,
            new GodModeController.NotifyRequest("Titulo", "Mensagem"),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        push.Verify(p => p.SendToGroupAsync(
            groupId,
            "Titulo",
            "Mensagem",
            It.Is<Dictionary<string, string>>(d => d["type"] == "admin_notification"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── NotifyUserAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyUserAsync_WhenTitleIsEmpty_ShouldReturnBadRequest()
    {
        var sut = CreateSut(dbName: nameof(NotifyUserAsync_WhenTitleIsEmpty_ShouldReturnBadRequest));

        var result = await sut.NotifyUserAsync(
            Guid.NewGuid(),
            new GodModeController.NotifyRequest("", "Body"),
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task NotifyUserAsync_WhenValid_ShouldCallSendToUserAsync()
    {
        var push   = new Mock<IPushService>();
        var userId = Guid.NewGuid();
        var sut    = CreateSut(push, dbName: nameof(NotifyUserAsync_WhenValid_ShouldCallSendToUserAsync));

        var result = await sut.NotifyUserAsync(
            userId,
            new GodModeController.NotifyRequest("Titulo", "Mensagem"),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        push.Verify(p => p.SendToUserAsync(
            userId,
            "Titulo",
            "Mensagem",
            It.Is<Dictionary<string, string>>(d => d["type"] == "admin_notification"),
            It.IsAny<CancellationToken>(),
            null),
            Times.Once);
    }

    // ── NotifyAllUsersAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task NotifyAllUsersAsync_WhenTitleIsEmpty_ShouldReturnBadRequest()
    {
        var sut = CreateSut(dbName: nameof(NotifyAllUsersAsync_WhenTitleIsEmpty_ShouldReturnBadRequest));

        var result = await sut.NotifyAllUsersAsync(
            new GodModeController.NotifyRequest("", "Body"),
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task NotifyAllUsersAsync_WhenBodyIsWhitespace_ShouldReturnBadRequest()
    {
        var sut = CreateSut(dbName: nameof(NotifyAllUsersAsync_WhenBodyIsWhitespace_ShouldReturnBadRequest));

        var result = await sut.NotifyAllUsersAsync(
            new GodModeController.NotifyRequest("Title", "   "),
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task NotifyAllUsersAsync_WhenNoActiveUsers_ShouldReturnOkWithZeroSent()
    {
        var push = new Mock<IPushService>();
        var db   = DbContextFactory.Create(nameof(NotifyAllUsersAsync_WhenNoActiveUsers_ShouldReturnOkWithZeroSent));

        // Somente um usuário inativo
        db.Users.Add(MakeInactiveUser("a"));
        await db.SaveChangesAsync();

        var sut = CreateSut(push, db);

        var result = await sut.NotifyAllUsersAsync(
            new GodModeController.NotifyRequest("Titulo", "Corpo"),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        push.Verify(p => p.SendToUsersAsync(
            It.IsAny<List<Guid>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()),
            Times.Never,
            "não deve chamar push se não há usuários ativos.");
    }

    [Fact]
    public async Task NotifyAllUsersAsync_ShouldOnlySendToActiveUsers()
    {
        var push = new Mock<IPushService>();
        var db   = DbContextFactory.Create(nameof(NotifyAllUsersAsync_ShouldOnlySendToActiveUsers));

        var active1  = MakeActiveUser("1");
        var active2  = MakeActiveUser("2");
        var inactive = MakeInactiveUser("3");
        db.Users.AddRange(active1, active2, inactive);
        await db.SaveChangesAsync();

        var sut = CreateSut(push, db);

        await sut.NotifyAllUsersAsync(
            new GodModeController.NotifyRequest("T", "B"),
            CancellationToken.None);

        push.Verify(p => p.SendToUsersAsync(
            It.Is<List<Guid>>(ids =>
                ids.Count == 2 &&
                ids.Contains(active1.Id) &&
                ids.Contains(active2.Id) &&
                !ids.Contains(inactive.Id)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyAllUsersAsync_ShouldPassBroadcastDataType()
    {
        var push = new Mock<IPushService>();
        var db   = DbContextFactory.Create(nameof(NotifyAllUsersAsync_ShouldPassBroadcastDataType));

        db.Users.Add(MakeActiveUser("x"));
        await db.SaveChangesAsync();

        var sut = CreateSut(push, db);

        await sut.NotifyAllUsersAsync(
            new GodModeController.NotifyRequest("Titulo", "Mensagem"),
            CancellationToken.None);

        push.Verify(p => p.SendToUsersAsync(
            It.IsAny<List<Guid>>(),
            "Titulo",
            "Mensagem",
            It.Is<Dictionary<string, string>>(d => d["type"] == "admin_broadcast"),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyAllUsersAsync_ShouldReturnSentCount()
    {
        var push = new Mock<IPushService>();
        var db   = DbContextFactory.Create(nameof(NotifyAllUsersAsync_ShouldReturnSentCount));

        db.Users.AddRange(MakeActiveUser("a"), MakeActiveUser("b"), MakeActiveUser("c"));
        await db.SaveChangesAsync();

        var sut = CreateSut(push, db);

        var result = await sut.NotifyAllUsersAsync(
            new GodModeController.NotifyRequest("T", "B"),
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        // o resultado anônimo tem propriedade "sent"
        var sent = (int)value.GetType().GetProperty("sent")!.GetValue(value)!;
        sent.Should().Be(3);
    }
}
