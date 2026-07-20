using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class PlayerMembershipServiceTests
{
    private static PlayerMembershipService Sut(AppDbContext db, IPushService? push = null)
        => new(db, push ?? Mock.Of<IPushService>(), Mock.Of<ILogger<PlayerMembershipService>>());

    private static UserEntity User(string name)
        => new(name, name, "Teste", $"{name}-{Guid.NewGuid():N}@test.com", "hash", null, null);

    [Fact]
    public async Task UnlinkPlayerFromGroupAsync_SelfLeave_WhenWrongUser_ReturnsForbidden()
    {
        await using var db = DbContextFactory.Create(nameof(UnlinkPlayerFromGroupAsync_SelfLeave_WhenWrongUser_ReturnsForbidden));
        var user = User("owner");
        var group = new GroupEntity("Patota", null, user.Id);
        var player = new PlayerEntity("Owner", user.Id, group.Id, 0m, false, false, Status.Active);
        db.AddRange(user, group, player);
        await db.SaveChangesAsync();

        var result = await Sut(db).UnlinkPlayerFromGroupAsync(
            player.Id,
            PlayerUnlinkReason.SelfLeave,
            Guid.NewGuid(),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
        (await db.Players.FindAsync(player.Id))!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task UnlinkPlayerFromGroupAsync_SetsGuestClearsUserAndRemovesRolesOnlyInThatGroup()
    {
        await using var db = DbContextFactory.Create(nameof(UnlinkPlayerFromGroupAsync_SetsGuestClearsUserAndRemovesRolesOnlyInThatGroup));
        var user = User("player");
        var group = new GroupEntity("Patota A", null, user.Id);
        var otherGroup = new GroupEntity("Patota B", null, Guid.NewGuid());
        var player = new PlayerEntity("Jogador", user.Id, group.Id, 0m, false, false, Status.Active);
        db.AddRange(user, group, otherGroup, player);
        db.GroupAdmins.AddRange(
            new GroupAdminEntity { GroupId = group.Id, UserId = user.Id },
            new GroupAdminEntity { GroupId = otherGroup.Id, UserId = user.Id });
        db.GroupFinanceiros.AddRange(
            new GroupFinanceiroEntity { GroupId = group.Id, UserId = user.Id },
            new GroupFinanceiroEntity { GroupId = otherGroup.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        var result = await Sut(db).UnlinkPlayerFromGroupAsync(
            player.Id,
            PlayerUnlinkReason.SelfLeave,
            user.Id,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        saved.IsGuest.Should().BeTrue();
        saved.UserId.Should().BeNull();
        (await db.GroupAdmins.AnyAsync(x => x.GroupId == group.Id && x.UserId == user.Id)).Should().BeFalse();
        (await db.GroupFinanceiros.AnyAsync(x => x.GroupId == group.Id && x.UserId == user.Id)).Should().BeFalse();
        (await db.GroupAdmins.AnyAsync(x => x.GroupId == otherGroup.Id && x.UserId == user.Id)).Should().BeTrue();
        (await db.GroupFinanceiros.AnyAsync(x => x.GroupId == otherGroup.Id && x.UserId == user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkPlayerFromGroupAsync_AdminRemove_NotifiesAdminsAndRemovedUser()
    {
        await using var db = DbContextFactory.Create(nameof(UnlinkPlayerFromGroupAsync_AdminRemove_NotifiesAdminsAndRemovedUser));
        var user = User("removed");
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        var player = new PlayerEntity("Removido", user.Id, group.Id, 0m, false, false, Status.Active);
        db.AddRange(user, group, player);
        await db.SaveChangesAsync();
        var push = new Mock<IPushService>();

        var result = await Sut(db, push.Object).UnlinkPlayerFromGroupAsync(
            player.Id,
            PlayerUnlinkReason.AdminRemove,
            requestingUserId: null,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        push.Verify(p => p.SendToGroupAdminsAsync(
            group.Id,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<Dictionary<string, string>?>(d => d != null && d["type"] == "player_removed"),
            It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(p => p.SendToUserAsync(
            user.Id,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<Dictionary<string, string>?>(d => d != null && d["type"] == "removed_from_group"),
            It.IsAny<CancellationToken>(),
            group.Id), Times.Once);
    }

    [Fact]
    public async Task UnlinkUserFromAllGroupsAsync_UnlinksEveryLinkedPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(UnlinkUserFromAllGroupsAsync_UnlinksEveryLinkedPlayer));
        var user = User("multi");
        var groupA = new GroupEntity("A", null, Guid.NewGuid());
        var groupB = new GroupEntity("B", null, Guid.NewGuid());
        var playerA = new PlayerEntity("A", user.Id, groupA.Id, 0m, false, false, Status.Active);
        var playerB = new PlayerEntity("B", user.Id, groupB.Id, 0m, false, false, Status.Active);
        db.AddRange(user, groupA, groupB, playerA, playerB);
        await db.SaveChangesAsync();

        var result = await Sut(db).UnlinkUserFromAllGroupsAsync(
            user.Id,
            PlayerUnlinkReason.AccountDeletion,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        (await db.Players.IgnoreQueryFilters().Where(p => p.UserId == user.Id).CountAsync()).Should().Be(0);
        (await db.Players.IgnoreQueryFilters().CountAsync(p => p.IsGuest && (p.Id == playerA.Id || p.Id == playerB.Id))).Should().Be(2);
    }
}
