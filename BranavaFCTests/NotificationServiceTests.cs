using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

public class NotificationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NotificationService Sut(BratnavaFC.Infrastructure.Data.AppDbContext db)
        => new(db);

    private static UserNotificationEntity MakeNotif(
        Guid    userId,
        Guid?   groupId  = null,
        string  title    = "Título",
        string  body     = "Corpo",
        string? type     = null,
        bool    isRead   = false)
    {
        var n = new UserNotificationEntity(userId, groupId, title, body, type, null);
        if (isRead) n.MarkAsRead();
        return n;
    }

    // ── GetMineAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMineAsync_WhenNoNotifications_ReturnsEmptyPage()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_WhenNoNotifications_ReturnsEmptyPage));
        var sut = Sut(db);

        var result = await sut.GetMineAsync(Guid.NewGuid(), null, 1, 30, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Data.Should().BeEmpty();
        result.Data.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetMineAsync_FiltersToCurrentUser()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_FiltersToCurrentUser));

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        db.UserNotifications.AddRange(
            MakeNotif(userA, title: "Para A"),
            MakeNotif(userB, title: "Para B"));
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.GetMineAsync(userA, null, 1, 30, CancellationToken.None);

        result.Data!.Data.Should().HaveCount(1);
        result.Data.Data[0].Title.Should().Be("Para A");
        result.Data.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetMineAsync_WithGroupIdFilter_OnlyReturnsMatchingGroup()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_WithGroupIdFilter_OnlyReturnsMatchingGroup));

        var userId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();

        db.UserNotifications.AddRange(
            MakeNotif(userId, groupA, "Grupo A"),
            MakeNotif(userId, groupB, "Grupo B"),
            MakeNotif(userId, null,   "Sem grupo"));
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.GetMineAsync(userId, groupA, 1, 30, CancellationToken.None);

        result.Data!.Data.Should().HaveCount(1);
        result.Data.Data[0].Title.Should().Be("Grupo A");
    }

    [Fact]
    public async Task GetMineAsync_MapsAllDtoFieldsCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_MapsAllDtoFieldsCorrectly));

        var userId  = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var n = new UserNotificationEntity(userId, groupId, "Título", "Corpo", "match_invite", "{\"matchId\":\"abc\"}");
        db.UserNotifications.Add(n);
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetMineAsync(userId, null, 1, 30, CancellationToken.None);

        result.Data!.Data.Should().HaveCount(1);
        var dto = result.Data.Data[0];
        dto.Id.Should().Be(n.Id);
        dto.Title.Should().Be("Título");
        dto.Body.Should().Be("Corpo");
        dto.Type.Should().Be("match_invite");
        dto.DataJson.Should().Be("{\"matchId\":\"abc\"}");
        dto.IsRead.Should().BeFalse();
        dto.GroupId.Should().Be(groupId);
    }

    [Fact]
    public async Task GetMineAsync_Pagination_ReturnsCorrectSlice()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_Pagination_ReturnsCorrectSlice));

        var userId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
            db.UserNotifications.Add(MakeNotif(userId, title: $"N{i}"));
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var page1 = await sut.GetMineAsync(userId, null, page: 1, pageSize: 2, CancellationToken.None);
        var page2 = await sut.GetMineAsync(userId, null, page: 2, pageSize: 2, CancellationToken.None);
        var page3 = await sut.GetMineAsync(userId, null, page: 3, pageSize: 2, CancellationToken.None);

        page1.Data!.Total.Should().Be(5);
        page1.Data.Data.Should().HaveCount(2);
        page2.Data!.Data.Should().HaveCount(2);
        page3.Data!.Data.Should().HaveCount(1);
    }

    // ── GetUnreadCountAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsOnlyUnreadForUser()
    {
        await using var db = DbContextFactory.Create(nameof(GetUnreadCountAsync_ReturnsOnlyUnreadForUser));

        var userId = Guid.NewGuid();
        var other  = Guid.NewGuid();

        db.UserNotifications.AddRange(
            MakeNotif(userId, isRead: false),
            MakeNotif(userId, isRead: false),
            MakeNotif(userId, isRead: true),
            MakeNotif(other,  isRead: false)); // outro usuário
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.GetUnreadCountAsync(userId, null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(2);
    }

    [Fact]
    public async Task GetUnreadCountAsync_WithGroupId_OnlyCountsMatchingGroup()
    {
        await using var db = DbContextFactory.Create(nameof(GetUnreadCountAsync_WithGroupId_OnlyCountsMatchingGroup));

        var userId = Guid.NewGuid();
        var gA     = Guid.NewGuid();
        var gB     = Guid.NewGuid();

        db.UserNotifications.AddRange(
            MakeNotif(userId, gA, isRead: false),
            MakeNotif(userId, gB, isRead: false));
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.GetUnreadCountAsync(userId, gA, CancellationToken.None);

        result.Data.Should().Be(1);
    }

    // ── MarkReadAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkReadAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(MarkReadAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.MarkReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task MarkReadAsync_WhenOwnerMismatch_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(MarkReadAsync_WhenOwnerMismatch_ReturnsNotFound));

        var owner  = Guid.NewGuid();
        var n = MakeNotif(owner);
        db.UserNotifications.Add(n);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.MarkReadAsync(n.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task MarkReadAsync_WhenValid_SetsIsReadTrue()
    {
        await using var db = DbContextFactory.Create(nameof(MarkReadAsync_WhenValid_SetsIsReadTrue));

        var userId = Guid.NewGuid();
        var n = MakeNotif(userId, isRead: false);
        db.UserNotifications.Add(n);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.MarkReadAsync(n.Id, userId, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.UserNotifications.FirstAsync(x => x.Id == n.Id);
        reloaded.IsRead.Should().BeTrue();
        reloaded.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkReadAsync_WhenAlreadyRead_RemainsIdempotent()
    {
        await using var db = DbContextFactory.Create(nameof(MarkReadAsync_WhenAlreadyRead_RemainsIdempotent));

        var userId = Guid.NewGuid();
        var n = MakeNotif(userId, isRead: true);
        db.UserNotifications.Add(n);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.MarkReadAsync(n.Id, userId, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.UserNotifications.FirstAsync(x => x.Id == n.Id);
        reloaded.IsRead.Should().BeTrue();
    }

    // ── MarkAllReadAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllReadAsync_MarksAllUnreadForUser()
    {
        await using var db = DbContextFactory.Create(nameof(MarkAllReadAsync_MarksAllUnreadForUser));

        var userId = Guid.NewGuid();
        var other  = Guid.NewGuid();

        db.UserNotifications.AddRange(
            MakeNotif(userId, isRead: false),
            MakeNotif(userId, isRead: false),
            MakeNotif(userId, isRead: true),
            MakeNotif(other,  isRead: false)); // deve ficar intacta
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.MarkAllReadAsync(userId, null, CancellationToken.None);

        result.Success.Should().BeTrue();
        var unreadUser  = await db.UserNotifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        var unreadOther = await db.UserNotifications.CountAsync(n => n.UserId == other  && !n.IsRead);
        unreadUser.Should().Be(0);
        unreadOther.Should().Be(1);
    }

    [Fact]
    public async Task MarkAllReadAsync_WithGroupId_OnlyMarksMatchingGroup()
    {
        await using var db = DbContextFactory.Create(nameof(MarkAllReadAsync_WithGroupId_OnlyMarksMatchingGroup));

        var userId = Guid.NewGuid();
        var gA     = Guid.NewGuid();
        var gB     = Guid.NewGuid();

        db.UserNotifications.AddRange(
            MakeNotif(userId, gA, isRead: false),
            MakeNotif(userId, gB, isRead: false));
        await db.SaveChangesAsync();

        var sut = Sut(db);
        await sut.MarkAllReadAsync(userId, gA, CancellationToken.None);

        var stillUnread = await db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        stillUnread.Should().HaveCount(1);
        stillUnread[0].GroupId.Should().Be(gB);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsOkIdempotent()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenNotFound_ReturnsOkIdempotent));
        var sut = Sut(db);

        var result = await sut.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenOwnerMismatch_ReturnsOkWithoutDeleting()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenOwnerMismatch_ReturnsOkWithoutDeleting));

        var owner = Guid.NewGuid();
        var n = MakeNotif(owner);
        db.UserNotifications.Add(n);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.DeleteAsync(n.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var still = await db.UserNotifications.FindAsync(n.Id);
        still.Should().NotBeNull("outra pessoa não pode apagar a notificação alheia");
    }

    [Fact]
    public async Task DeleteAsync_WhenValid_RemovesRow()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenValid_RemovesRow));

        var userId = Guid.NewGuid();
        var n = MakeNotif(userId);
        db.UserNotifications.Add(n);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var result = await sut.DeleteAsync(n.Id, userId, CancellationToken.None);

        result.Success.Should().BeTrue();
        var deleted = await db.UserNotifications.FindAsync(n.Id);
        deleted.Should().BeNull();
    }
}
