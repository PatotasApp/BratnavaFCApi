using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Absences;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

/// <summary>
/// Testa a sincronização entre ausências e partidas já existentes:
/// criar ausência recusa presença nas partidas do período; editar reaplica;
/// excluir reverte os convites recusados automaticamente.
/// </summary>
public sealed class AbsenceServiceMatchSyncTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static AbsenceService CreateSut(AppDbContext db) => new(db);

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

    /// <summary>Partida em Acceptation com os jogadores informados (convites pendentes).</summary>
    private static async Task<MatchEntity> SeedMatchAsync(
        AppDbContext db, Guid groupId, DateTime playedAt, params PlayerEntity[] players)
    {
        var match = new MatchEntity(groupId, playedAt, "Campo");
        foreach (var p in players)
            match.AddPlayer(new MatchPlayerEntity(p.Id), p);
        match.OpenAcceptation();
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        return match;
    }

    private static CreateAbsenceDto Dto(DateOnly start, DateOnly end, AbsenceType type = AbsenceType.Travel)
        => new(start, end, type, null);

    private static DateOnly Day(int offsetDays) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offsetDays));

    private static Task<MatchPlayerEntity> GetMp(AppDbContext db, Guid matchId, Guid playerId)
        => db.MatchPlayers.FirstAsync(mp => mp.MatchId == matchId && mp.PlayerId == playerId);

    // ─── CreateAsync → partidas existentes ────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenUpcomingMatchInsidePeriod_ShouldAutoRejectPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenUpcomingMatchInsidePeriod_ShouldAutoRejectPlayer));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), player);

        var sut    = CreateSut(db);
        var result = await sut.CreateAsync(user.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1 partida");

        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(result.Data.Id);
    }

    [Fact]
    public async Task CreateAsync_WhenPlayerAlreadyAccepted_ShouldOverrideToRejected()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenPlayerAlreadyAccepted_ShouldOverrideToRejected));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), player);

        match.AcceptInvite(player.Id);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.CreateAsync(user.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenMatchOutsidePeriod_ShouldNotTouchInvite()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenMatchOutsidePeriod_ShouldNotTouchInvite));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(20), player);

        var sut = CreateSut(db);
        await sut.CreateAsync(user.Id, Dto(Day(5), Day(8)), CancellationToken.None);

        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.None);
        mp.AutoRejectedByAbsenceId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenMatchUtcFallsOnAbsenceDateInSaoPaulo_ShouldAutoRejectPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenMatchUtcFallsOnAbsenceDateInSaoPaulo_ShouldAutoRejectPlayer));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(
            db,
            group.Id,
            new DateTime(2026, 7, 18, 2, 0, 0, DateTimeKind.Utc),
            player);

        var sut = CreateSut(db);
        var result = await sut.CreateAsync(
            user.Id,
            Dto(new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 17)),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(result.Data.Id);
    }

    [Fact]
    public void ToSaoPauloDateBoundaryUtc_ShouldReturnUtcDateTime()
    {
        var boundary = AbsenceService.ToSaoPauloDateBoundaryUtc(new DateOnly(2026, 7, 17));

        boundary.Kind.Should().Be(DateTimeKind.Utc);
        boundary.Should().Be(new DateTime(2026, 7, 17, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CreateAsync_WhenMatchAlreadyStarted_ShouldNotTouchInvite()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenMatchAlreadyStarted_ShouldNotTouchInvite));
        var group = await SeedGroupAsync(db);
        var (user, p1) = await SeedPlayerAsync(db, group.Id, "P1");
        var (_, p2)    = await SeedPlayerAsync(db, group.Id, "P2");

        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), p1, p2);
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();
        match.Players.First(mp => mp.PlayerId == p1.Id).SetTeam(1);
        match.Players.First(mp => mp.PlayerId == p2.Id).SetTeam(2);
        match.Start();
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.CreateAsync(user.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        var mp = await GetMp(db, match.Id, p1.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Accepted);
        mp.AutoRejectedByAbsenceId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenMultipleMatchesInsidePeriod_ShouldRejectAll()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenMultipleMatchesInsidePeriod_ShouldRejectAll));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match1 = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(8),  player);
        var match2 = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(12), player);

        var sut    = CreateSut(db);
        var result = await sut.CreateAsync(user.Id, Dto(Day(7), Day(13)), CancellationToken.None);

        result.Message.Should().Contain("2 partidas");
        (await GetMp(db, match1.Id, player.Id)).InviteResponse.Should().Be(InviteResponse.Rejected);
        (await GetMp(db, match2.Id, player.Id)).InviteResponse.Should().Be(InviteResponse.Rejected);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotAffectOtherPlayersOfTheSameMatch()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_ShouldNotAffectOtherPlayersOfTheSameMatch));
        var group = await SeedGroupAsync(db);
        var (user1, p1) = await SeedPlayerAsync(db, group.Id, "P1");
        var (_, p2)     = await SeedPlayerAsync(db, group.Id, "P2");
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), p1, p2);

        var sut = CreateSut(db);
        await sut.CreateAsync(user1.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        (await GetMp(db, match.Id, p1.Id)).InviteResponse.Should().Be(InviteResponse.Rejected);
        (await GetMp(db, match.Id, p2.Id)).InviteResponse.Should().Be(InviteResponse.None);
    }

    // ─── UpdateAsync → reaplica nas partidas ──────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenPeriodMoves_ShouldRevertOldMatchAndRejectNewMatch()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenPeriodMoves_ShouldRevertOldMatchAndRejectNewMatch));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var earlyMatch = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(8),  player);
        var lateMatch  = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(20), player);

        var sut     = CreateSut(db);
        var created = await sut.CreateAsync(user.Id, Dto(Day(7), Day(9)), CancellationToken.None);

        // sanity: só a partida próxima foi rejeitada
        (await GetMp(db, earlyMatch.Id, player.Id)).InviteResponse.Should().Be(InviteResponse.Rejected);
        (await GetMp(db, lateMatch.Id,  player.Id)).InviteResponse.Should().Be(InviteResponse.None);

        // move o período para cobrir apenas a partida distante
        await sut.UpdateAsync(user.Id, created.Data.Id, Dto(Day(19), Day(21)), CancellationToken.None);

        var earlyMp = await GetMp(db, earlyMatch.Id, player.Id);
        earlyMp.InviteResponse.Should().Be(InviteResponse.None);
        earlyMp.AutoRejectedByAbsenceId.Should().BeNull();

        var lateMp = await GetMp(db, lateMatch.Id, player.Id);
        lateMp.InviteResponse.Should().Be(InviteResponse.Rejected);
        lateMp.AutoRejectedByAbsenceId.Should().Be(created.Data.Id);
    }

    [Fact]
    public async Task UpdateAsync_WhenMatchStillInsidePeriod_ShouldKeepRejection()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenMatchStillInsidePeriod_ShouldKeepRejection));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), player);

        var sut     = CreateSut(db);
        var created = await sut.CreateAsync(user.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        // amplia o período — a partida continua coberta
        await sut.UpdateAsync(user.Id, created.Data.Id, Dto(Day(8), Day(14)), CancellationToken.None);

        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(created.Data.Id);
    }

    // ─── DeleteAsync → reverte convites ───────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ShouldRevertAutoRejectedInvitesToPending()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_ShouldRevertAutoRejectedInvitesToPending));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), player);

        var sut     = CreateSut(db);
        var created = await sut.CreateAsync(user.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        await sut.DeleteAsync(user.Id, created.Data.Id, CancellationToken.None);

        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.None);
        mp.AutoRejectedByAbsenceId.Should().BeNull();
        db.UserAbsences.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotRevertManualRejections()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_ShouldNotRevertManualRejections));
        var group = await SeedGroupAsync(db);
        var (user, player) = await SeedPlayerAsync(db, group.Id);
        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(20), player);

        // rejeição manual, fora do período da ausência
        match.RejectInvite(player.Id);
        await db.SaveChangesAsync();

        var sut     = CreateSut(db);
        var created = await sut.CreateAsync(user.Id, Dto(Day(5), Day(8)), CancellationToken.None);
        await sut.DeleteAsync(user.Id, created.Data.Id, CancellationToken.None);

        var mp = await GetMp(db, match.Id, player.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected); // rejeição manual preservada
    }

    [Fact]
    public async Task DeleteAsync_WhenMatchAlreadyStarted_ShouldKeepRejection()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenMatchAlreadyStarted_ShouldKeepRejection));
        var group = await SeedGroupAsync(db);
        var (user, p1) = await SeedPlayerAsync(db, group.Id, "Ausente");
        var (_, p2)    = await SeedPlayerAsync(db, group.Id, "P2");
        var (_, p3)    = await SeedPlayerAsync(db, group.Id, "P3");

        var match = await SeedMatchAsync(db, group.Id, DateTime.UtcNow.AddDays(10), p1, p2, p3);

        var sut     = CreateSut(db);
        var created = await sut.CreateAsync(user.Id, Dto(Day(9), Day(11)), CancellationToken.None);

        // partida segue o fluxo e inicia sem o jogador ausente
        match.AcceptInvite(p2.Id);
        match.AcceptInvite(p3.Id);
        match.GoToMatchMaking();
        match.Players.First(mp => mp.PlayerId == p2.Id).SetTeam(1);
        match.Players.First(mp => mp.PlayerId == p3.Id).SetTeam(2);
        match.Start();
        await db.SaveChangesAsync();

        await sut.DeleteAsync(user.Id, created.Data.Id, CancellationToken.None);

        // partida já iniciada: rejeição não é revertida
        var mp = await GetMp(db, match.Id, p1.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
    }
}
