using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class PlayerServiceCoverageTests
{
    private static Mock<IMatchService> MatchServiceMock()
    {
        var mock = new Mock<IMatchService>();
        mock.Setup(m => m.SyncPlayerIntoActiveMatchesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        return mock;
    }

    private static PlayerService Sut(AppDbContext db, IMatchService? matchService = null, IPushService? push = null)
        => new(
            new RepositoryBase<PlayerEntity>(db),
            Mock.Of<ILogger<PlayerService>>(),
            db,
            push ?? Mock.Of<IPushService>(),
            matchService ?? MatchServiceMock().Object,
            TestImageStorage.Create());

    private static MatchEntity MatchWithStatus(Guid groupId, MatchStatus status)
    {
        var m = new MatchEntity(groupId, DateTime.UtcNow.AddHours(2), "Arena");
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.Status))!.SetValue(m, status);
        return m;
    }

    private static MatchPlayerEntity LinkMatchPlayer(Guid matchId, Guid groupId, Guid playerId)
    {
        var mp = new MatchPlayerEntity(playerId);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.MatchId))!.SetValue(mp, matchId);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.GroupId))!.SetValue(mp, groupId);
        return mp;
    }

    // ─── CreateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_GuestWithoutUser_SkipsUserValidation_AndSetsRatings()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_GuestWithoutUser_SkipsUserValidation_AndSetsRatings));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var req = new CreatePlayerDto(
            Name: "Convidado",
            UserId: null,
            GroupId: group.Id,
            SkillPoints: 3m,
            IsGoalkeeper: false,
            IsGuest: true,
            Status: Status.Active,
            GuestStarRating: 4,
            AttackRating: 7,
            DefenseRating: 6,
            OverallRating: 8);

        var result = await sut.CreateAsync(req, CancellationToken.None);

        result.Success.Should().BeTrue();
        var created = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == result.Data!.Id);
        created.IsGuest.Should().BeTrue();
        created.UserId.Should().BeNull();
        created.GuestStarRating.Should().Be(4);
        created.AttackRating.Should().Be(7);
        created.DefenseRating.Should().Be(6);
        created.OverallRating.Should().Be(8);
    }

    [Fact]
    public async Task CreateAsync_ActivePlayer_SyncsIntoActiveMatches()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_ActivePlayer_SyncsIntoActiveMatches));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var matchMock = MatchServiceMock();
        var sut = Sut(db, matchMock.Object);

        var req = new CreatePlayerDto("P", group.Id, null, 0m, false, true, Status.Active);
        var result = await sut.CreateAsync(req, CancellationToken.None);

        result.Success.Should().BeTrue();
        matchMock.Verify(m => m.SyncPlayerIntoActiveMatchesAsync(
            group.Id, result.Data!.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Note: CreateAsync's "inactive player skips sync" branch is unreachable —
    // PlayerEntity's constructor ignores the status argument and always creates Active players.

    // ─── UpdateAsync status transitions ─────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ActiveToInactive_InactivatesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_ActiveToInactive_InactivatesPlayer));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 1m, false, true, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var req = new UpdatePlayerDto("P", group.Id, 1m, false, true, Status.Inactive);

        var result = await sut.UpdateAsync(player.Id, req, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.Status.Should().Be(Status.Inactive);
    }

    [Fact]
    public async Task UpdateAsync_InactiveToActive_Reactivates_AndSyncsIntoMatches()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_InactiveToActive_Reactivates_AndSyncsIntoMatches));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 1m, false, true, Status.Active);
        player.Inactivate();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var matchMock = MatchServiceMock();
        var sut = Sut(db, matchMock.Object);
        var req = new UpdatePlayerDto("P", group.Id, 1m, false, true, Status.Active);

        var result = await sut.UpdateAsync(player.Id, req, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.Status.Should().Be(Status.Active);
        matchMock.Verify(m => m.SyncPlayerIntoActiveMatchesAsync(
            group.Id, player.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── DeleteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenPlayerNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenPlayerNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_WhenPlayerHasMatchHistory_ReturnsFailure()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenPlayerHasMatchHistory_ReturnsFailure));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 0m, false, true, Status.Active);
        db.Players.Add(player);

        var match = MatchWithStatus(group.Id, MatchStatus.MatchMaking);
        db.Matches.Add(match);
        db.MatchPlayers.Add(LinkMatchPlayer(match.Id, group.Id, player.Id));
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.DeleteAsync(player.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("histórico de partidas");
        (await db.Players.IgnoreQueryFilters().AnyAsync(p => p.Id == player.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WithOnlyPreGameInvites_RemovesInvites_AndDeletesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WithOnlyPreGameInvites_RemovesInvites_AndDeletesPlayer));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 0m, false, true, Status.Active);
        db.Players.Add(player);

        var match = MatchWithStatus(group.Id, MatchStatus.Created);
        db.Matches.Add(match);
        db.MatchPlayers.Add(LinkMatchPlayer(match.Id, group.Id, player.Id));
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.DeleteAsync(player.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Players.IgnoreQueryFilters().AnyAsync(p => p.Id == player.Id)).Should().BeFalse();
        (await db.MatchPlayers.AnyAsync(mp => mp.PlayerId == player.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WithNoInvites_DeletesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WithNoInvites_DeletesPlayer));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 0m, false, true, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.DeleteAsync(player.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Players.IgnoreQueryFilters().AnyAsync(p => p.Id == player.Id)).Should().BeFalse();
    }

    // ─── GetByIdAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WhenFound_ReturnsDto()
    {
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenFound_ReturnsDto));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("Caio", null, group.Id, 5m, true, true, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.GetByIdAsync(player.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Caio");
        result.Data.IsGoalkeeper.Should().BeTrue();
        result.Data.SkillPoints.Should().Be(5m);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ─── Inactivate / Reactivate ─────────────────────────────────────────────

    [Fact]
    public async Task InactivateAsync_WhenFound_InactivatesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenFound_InactivatesPlayer));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 0m, false, true, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.InactivateAsync(player.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id))
            .Status.Should().Be(Status.Inactive);
    }

    [Fact]
    public async Task InactivateAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.InactivateAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ReactivateAsync_WhenFound_Reactivates_AndSyncsIntoMatches()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateAsync_WhenFound_Reactivates_AndSyncsIntoMatches));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 0m, false, true, Status.Active);
        player.Inactivate();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var matchMock = MatchServiceMock();
        var sut = Sut(db, matchMock.Object);

        var result = await sut.ReactivateAsync(player.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id))
            .Status.Should().Be(Status.Active);
        matchMock.Verify(m => m.SyncPlayerIntoActiveMatchesAsync(
            group.Id, player.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReactivateAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.ReactivateAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ─── GetByUserIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByUserIdAsync_WhenEmptyGuid_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(GetByUserIdAsync_WhenEmptyGuid_ReturnsBadRequest));
        var sut = Sut(db);

        var result = await sut.GetByUserIdAsync(Guid.Empty, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsPlayersOrderedByGroupName()
    {
        await using var db = DbContextFactory.Create(nameof(GetByUserIdAsync_ReturnsPlayersOrderedByGroupName));
        var user = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Users.Add(user);
        var groupB = new GroupEntity("Bravo", null, Guid.NewGuid());
        var groupA = new GroupEntity("Alpha", null, Guid.NewGuid());
        db.Groups.AddRange(groupB, groupA);
        db.Players.Add(new PlayerEntity("P1", user.Id, groupB.Id, 0m, false, false, Status.Active));
        db.Players.Add(new PlayerEntity("P2", user.Id, groupA.Id, 0m, false, false, Status.Active));
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.GetByUserIdAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Select(p => p.GroupName).Should().ContainInOrder("Alpha", "Bravo");
    }

    // ─── GetBirthdayStatusAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetBirthdayStatusAsync_OrdersByProximity_AndPutsNoBirthdayLast()
    {
        await using var db = DbContextFactory.Create(nameof(GetBirthdayStatusAsync_OrdersByProximity_AndPutsNoBirthdayLast));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var soonBd = today.AddDays(10);
        var passedBd = today.AddDays(-10);

        var userSoon = new UserEntity("soon", "S", "S", "s@test.com", "hash", null,
            new DateTimeOffset(new DateTime(1990, soonBd.Month, soonBd.Day, 12, 0, 0, DateTimeKind.Utc)));
        var userPassed = new UserEntity("passed", "P", "P", "p@test.com", "hash", null,
            new DateTimeOffset(new DateTime(1990, passedBd.Month, passedBd.Day, 12, 0, 0, DateTimeKind.Utc)));
        var userNoBd = new UserEntity("nobd", "N", "N", "n@test.com", "hash", null, null);
        db.Users.AddRange(userSoon, userPassed, userNoBd);

        db.Players.Add(new PlayerEntity("Soon", userSoon.Id, group.Id, 0m, false, false, Status.Active));
        db.Players.Add(new PlayerEntity("Passed", userPassed.Id, group.Id, 0m, false, false, Status.Active));
        db.Players.Add(new PlayerEntity("NoBd", userNoBd.Id, group.Id, 0m, false, false, Status.Active));
        // Guests and inactive players are excluded
        db.Players.Add(new PlayerEntity("Guest", null, group.Id, 0m, false, true, Status.Active));
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.GetBirthdayStatusAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var items = result.Data!;
        items.Should().HaveCount(3);
        items[0].Name.Should().Be("Soon", "next birthday comes first");
        items[1].Name.Should().Be("Passed", "passed birthday wraps to next year");
        items[2].Name.Should().Be("NoBd", "players without birthday go last");
        items[0].HasBirthday.Should().BeTrue();
        items[0].BirthDate.Should().NotBeNull();
        items[2].HasBirthday.Should().BeFalse();
    }

    // ─── GetGroupIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetGroupIdAsync_WhenFound_ReturnsGroupId()
    {
        await using var db = DbContextFactory.Create(nameof(GetGroupIdAsync_WhenFound_ReturnsGroupId));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("P", null, group.Id, 0m, false, true, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        (await sut.GetGroupIdAsync(player.Id, CancellationToken.None)).Should().Be(group.Id);
    }

    [Fact]
    public async Task GetGroupIdAsync_WhenNotFound_ReturnsNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetGroupIdAsync_WhenNotFound_ReturnsNull));
        var sut = Sut(db);

        (await sut.GetGroupIdAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    // ─── LeaveGroupAsync push notification ───────────────────────────────────

    [Fact]
    public async Task LeaveGroupAsync_NotifiesGroupAdmins()
    {
        await using var db = DbContextFactory.Create(nameof(LeaveGroupAsync_NotifiesGroupAdmins));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);
        var player = new PlayerEntity("Fulano", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var push = new Mock<IPushService>();
        var sut = Sut(db, push: push.Object);

        var result = await sut.LeaveGroupAsync(player.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        push.Verify(p => p.SendToGroupAdminsAsync(
            group.Id,
            It.IsAny<string>(),
            It.Is<string>(b => b.Contains("Fulano")),
            It.Is<Dictionary<string, string>>(d => d["type"] == "player_left"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
