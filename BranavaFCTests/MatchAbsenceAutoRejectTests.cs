using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Testa a lógica de auto-rejeição por ausência no momento do sync de jogadores,
/// que ocorre ao criar uma partida ou ao chamar SyncPlayersFromGroupAsync.
/// </summary>
public sealed class MatchAbsenceAutoRejectTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Mock<IRepositoryBase<MatchEntity>> BuildRepoMock(AppDbContext db)
    {
        var repo = new Mock<IRepositoryBase<MatchEntity>>(MockBehavior.Strict);
        repo.Setup(r => r.Add(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Add(m));
        repo.Setup(r => r.Remove(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Remove(m));
        repo.Setup(r => r.Update(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(_ => { });
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => db.SaveChangesAsync(ct));
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches
                    .Include(m => m.Players).ThenInclude(mp => mp.Player)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));
        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches
                    .Include(m => m.Players).ThenInclude(mp => mp.Player)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));
        return repo;
    }

    private static MatchService CreateSut(AppDbContext db, Mock<IRepositoryBase<MatchEntity>> repo)
        => new(db, repo.Object, Mock.Of<IPushService>(), Mock.Of<IReplayUrlService>(), Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>());

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db)
    {
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<(UserEntity user, PlayerEntity player)> SeedPlayerAsync(
        AppDbContext db, Guid groupId, string name = "Jogador")
    {
        var user   = new UserEntity(name, name, "X", $"{name}@t.com", "hash", null, null);
        var player = new PlayerEntity(name, user.Id, groupId, 5m, false);
        db.Users.Add(user);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return (user, player);
    }

    private static async Task<UserAbsenceEntity> SeedAbsenceAsync(
        AppDbContext db, Guid userId, DateOnly start, DateOnly end,
        AbsenceType type = AbsenceType.Travel)
    {
        var absence = new UserAbsenceEntity(userId, start, end, type, null);
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();
        return absence;
    }

    // ─── Create (via SyncPlayersFromGroupCoreAsync) ───────────────────────────

    [Fact]
    public async Task Create_WhenPlayerHasAbsenceCoveringMatchDate_ShouldAutoRejectPlayer()
    {
        await using var db   = DbContextFactory.Create(nameof(Create_WhenPlayerHasAbsenceCoveringMatchDate_ShouldAutoRejectPlayer));
        var repo  = BuildRepoMock(db);
        var sut   = CreateSut(db, repo);
        var group = await SeedGroupAsync(db);

        var (user, _) = await SeedPlayerAsync(db, group.Id);

        var matchDate = DateTime.UtcNow.AddDays(7);
        var matchOnly = DateOnly.FromDateTime(matchDate);

        var absence = await SeedAbsenceAsync(db, user.Id, matchOnly.AddDays(-1), matchOnly.AddDays(1));

        var match  = new MatchEntity(group.Id, matchDate, "Campo");
        var result = await sut.Create(group.Id, match, CancellationToken.None);

        result.Success.Should().BeTrue();

        var mp = await db.MatchPlayers.FirstAsync();
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(absence.Id);
    }

    [Fact]
    public async Task Create_WhenPlayerHasNoAbsenceForMatchDate_ShouldLeaveResponseAsNone()
    {
        await using var db   = DbContextFactory.Create(nameof(Create_WhenPlayerHasNoAbsenceForMatchDate_ShouldLeaveResponseAsNone));
        var repo  = BuildRepoMock(db);
        var sut   = CreateSut(db, repo);
        var group = await SeedGroupAsync(db);

        var (user, _) = await SeedPlayerAsync(db, group.Id);

        var matchDate = DateTime.UtcNow.AddDays(10);
        var matchOnly = DateOnly.FromDateTime(matchDate);

        // ausência termina antes da partida
        await SeedAbsenceAsync(db, user.Id, matchOnly.AddDays(-5), matchOnly.AddDays(-2));

        var match  = new MatchEntity(group.Id, matchDate, "Campo");
        var result = await sut.Create(group.Id, match, CancellationToken.None);

        result.Success.Should().BeTrue();

        var mp = await db.MatchPlayers.FirstAsync();
        mp.InviteResponse.Should().Be(InviteResponse.None);
        mp.AutoRejectedByAbsenceId.Should().BeNull();
    }

    [Fact]
    public async Task Create_WhenMatchDateExactlyOnAbsenceBoundary_ShouldAutoReject()
    {
        await using var db   = DbContextFactory.Create(nameof(Create_WhenMatchDateExactlyOnAbsenceBoundary_ShouldAutoReject));
        var repo  = BuildRepoMock(db);
        var sut   = CreateSut(db, repo);
        var group = await SeedGroupAsync(db);

        var (user, _) = await SeedPlayerAsync(db, group.Id);

        var matchDate = DateTime.UtcNow.AddDays(5);
        var matchOnly = DateOnly.FromDateTime(matchDate);

        // ausência é exatamente o mesmo dia da partida
        var absence = await SeedAbsenceAsync(db, user.Id, matchOnly, matchOnly);

        var match  = new MatchEntity(group.Id, matchDate, "Campo");
        await sut.Create(group.Id, match, CancellationToken.None);

        var mp = await db.MatchPlayers.FirstAsync();
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(absence.Id);
    }

    [Fact]
    public async Task Create_WhenOnlyOneOfTwoPlayersHasAbsence_ShouldRejectOnlyThatPlayer()
    {
        await using var db   = DbContextFactory.Create(nameof(Create_WhenOnlyOneOfTwoPlayersHasAbsence_ShouldRejectOnlyThatPlayer));
        var repo  = BuildRepoMock(db);
        var sut   = CreateSut(db, repo);
        var group = await SeedGroupAsync(db);

        var (user1, player1) = await SeedPlayerAsync(db, group.Id, "Jogador1");
        var (_, player2)     = await SeedPlayerAsync(db, group.Id, "Jogador2");

        var matchDate = DateTime.UtcNow.AddDays(7);
        var matchOnly = DateOnly.FromDateTime(matchDate);

        // apenas user1 tem ausência
        var absence = await SeedAbsenceAsync(db, user1.Id, matchOnly, matchOnly);

        var match  = new MatchEntity(group.Id, matchDate, "Campo");
        await sut.Create(group.Id, match, CancellationToken.None);

        var mp1 = await db.MatchPlayers.FirstAsync(mp => mp.PlayerId == player1.Id);
        var mp2 = await db.MatchPlayers.FirstAsync(mp => mp.PlayerId == player2.Id);

        mp1.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp1.AutoRejectedByAbsenceId.Should().Be(absence.Id);

        mp2.InviteResponse.Should().Be(InviteResponse.None);
        mp2.AutoRejectedByAbsenceId.Should().BeNull();
    }

    [Fact]
    public async Task Create_WhenPlayerIsGuestWithNoUserId_ShouldNotBeAutoRejected()
    {
        await using var db   = DbContextFactory.Create(nameof(Create_WhenPlayerIsGuestWithNoUserId_ShouldNotBeAutoRejected));
        var repo  = BuildRepoMock(db);
        var sut   = CreateSut(db, repo);
        var group = await SeedGroupAsync(db);

        // convidado sem userId
        var guest = new PlayerEntity("Convidado", null, group.Id, 3m, false, isGuest: true);
        db.Players.Add(guest);
        await db.SaveChangesAsync();

        var matchDate = DateTime.UtcNow.AddDays(5);
        var match     = new MatchEntity(group.Id, matchDate, "Campo");
        await sut.Create(group.Id, match, CancellationToken.None);

        var mp = await db.MatchPlayers.FirstAsync();
        mp.InviteResponse.Should().Be(InviteResponse.None);
        mp.AutoRejectedByAbsenceId.Should().BeNull();
    }

}
