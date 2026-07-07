using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

public class FinancialTransactionCoverageTests
{
    private static FinancialTransactionService MakeSut(AppDbContext db) => new(db);

    private sealed record Seed(GroupEntity Group, UserEntity User, PlayerEntity Player);

    private static async Task<Seed> SeedGroupWithPlayerAsync(
        AppDbContext db,
        decimal? monthlyFee = null,
        decimal? goalkeeperFee = null,
        bool goalkeeper = false,
        DateTime? joinedAt = null)
    {
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user = new UserEntity("u" + Guid.NewGuid().ToString("N")[..6], "F", "L",
            $"{Guid.NewGuid():N}@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, goalkeeper, false, Status.Active);
        if (joinedAt.HasValue) player.SetJoinedAt(joinedAt.Value);
        db.Players.Add(player);

        if (monthlyFee.HasValue || goalkeeperFee.HasValue)
        {
            var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
            if (monthlyFee.HasValue)    settings.SetMonthlyFee(monthlyFee.Value);
            if (goalkeeperFee.HasValue) settings.SetGoalkeeperMonthlyFee(goalkeeperFee.Value);
            db.GroupSettings.Add(settings);
        }

        await db.SaveChangesAsync();
        return new Seed(group, user, player);
    }

    // ── GetPendingTotalsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingTotals_NoPlayers_SumsOnlyExtras()
    {
        await using var db = DbContextFactory.Create(nameof(GetPendingTotals_NoPlayers_SumsOnlyExtras));
        var groupId = Guid.NewGuid();

        db.Groups.Add(new GroupEntity("G", null, Guid.NewGuid()));

        // Cobrança extra pendente com desconto parcial (40 - 10 = 30)
        var charge  = new ExtraChargeEntity(groupId, "C", null, 40m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, Guid.NewGuid(), groupId, 40m);
        payment.ApplyDiscount(10m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetPendingTotalsAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data!.TotalMonthlyPending.Should().Be(0m);
        result.Data.TotalExtraChargesPending.Should().Be(30m);
        result.Data.GrandTotal.Should().Be(30m);
    }

    [Fact]
    public async Task GetPendingTotals_MixesExplicitAndVirtualMonthlyPending()
    {
        await using var db = DbContextFactory.Create(nameof(GetPendingTotals_MixesExplicitAndVirtualMonthlyPending));
        var today = DateTime.UtcNow;
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m,
            joinedAt: new DateTime(today.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Mês 1: pendente explícito com desconto parcial (100 - 40 = 60)
        var rec = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, today.Year, 1, 100m);
        rec.ApplyDiscount(40m, null, Guid.NewGuid());
        db.MonthlyPayments.Add(rec);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetPendingTotalsAsync(seed.Group.Id);

        result.Success.Should().BeTrue();
        // Mês 1 explícito = 60; meses 2..atual sem registro = 100 cada
        var expected = 60m + (today.Month - 1) * 100m;
        result.Data!.TotalMonthlyPending.Should().Be(expected);
    }

    [Fact]
    public async Task GetPendingTotals_PaidMonth_NotCounted()
    {
        await using var db = DbContextFactory.Create(nameof(GetPendingTotals_PaidMonth_NotCounted));
        var today = DateTime.UtcNow;
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m); // joined now → só o mês atual

        var rec = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, today.Year, today.Month, 100m);
        rec.MarkAsPaid(null, null, null, null);
        db.MonthlyPayments.Add(rec);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetPendingTotalsAsync(seed.Group.Id);

        result.Data!.TotalMonthlyPending.Should().Be(0m, "único mês devido está pago");
    }

    [Fact]
    public async Task GetPendingTotals_PlayerJoinsInFuture_ContributesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(GetPendingTotals_PlayerJoinsInFuture_ContributesNothing));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m,
            joinedAt: DateTime.UtcNow.AddYears(1));

        var result = await MakeSut(db).GetPendingTotalsAsync(seed.Group.Id);

        result.Data!.TotalMonthlyPending.Should().Be(0m);
    }

    [Fact]
    public async Task GetPendingTotals_NoFeeAndNoRecords_SkipsPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(GetPendingTotals_NoFeeAndNoRecords_SkipsPlayer));
        var seed = await SeedGroupWithPlayerAsync(db); // sem settings

        var result = await MakeSut(db).GetPendingTotalsAsync(seed.Group.Id);

        result.Data!.TotalMonthlyPending.Should().Be(0m);
        result.Data.GrandTotal.Should().Be(0m);
    }

    [Fact]
    public async Task GetPendingTotals_Goalkeeper_UsesGoalkeeperFee()
    {
        await using var db = DbContextFactory.Create(nameof(GetPendingTotals_Goalkeeper_UsesGoalkeeperFee));
        var seed = await SeedGroupWithPlayerAsync(db,
            monthlyFee: 100m, goalkeeperFee: 60m, goalkeeper: true); // joined now → 1 mês virtual

        var result = await MakeSut(db).GetPendingTotalsAsync(seed.Group.Id);

        result.Data!.TotalMonthlyPending.Should().Be(60m);
    }

    // ── SyncPaidPaymentsAsync — cobranças extras ──────────────────────────────

    [Fact]
    public async Task Sync_OrphanPaidExtra_CreatesTransaction()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_OrphanPaidExtra_CreatesTransaction));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "Churrasco", null, 45m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 45m);
        payment.MarkAsPaid(null, null, null, null);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).SyncPaidPaymentsAsync(seed.Group.Id);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().Be(1);

        var tx = await db.GroupTransactions.SingleAsync();
        tx.SourceType.Should().Be(TransactionSourceType.ExtraCharge);
        tx.SourceId.Should().Be(payment.Id);
        tx.Amount.Should().Be(45m);
        tx.Description.Should().Contain("Churrasco").And.Contain("P");
    }

    [Fact]
    public async Task Sync_ExtraFullDiscountWithExistingTx_RemovesEntry()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_ExtraFullDiscountWithExistingTx_RemovesEntry));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 45m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 45m);
        payment.MarkAsPaid(null, null, null, null);
        payment.ApplyDiscount(45m, "total", Guid.NewGuid()); // effective 0
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);

        // Entrada obsoleta no caixa
        db.GroupTransactions.Add(new GroupTransactionEntity(
            seed.Group.Id, 45m, "C – P", DateOnly.FromDateTime(DateTime.UtcNow),
            TransactionSourceType.ExtraCharge, payment.Id, "P"));
        await db.SaveChangesAsync();

        var result = await MakeSut(db).SyncPaidPaymentsAsync(seed.Group.Id);

        result.Success.Should().BeTrue();
        result.Data!.Updated.Should().Be(1);
        (await db.GroupTransactions.CountAsync()).Should().Be(0, "desconto total remove a entrada");
    }

    [Fact]
    public async Task Sync_ExtraStaleAmount_UpdatesEntry()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_ExtraStaleAmount_UpdatesEntry));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 50m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 50m);
        payment.MarkAsPaid(null, null, null, null);
        payment.ApplyDiscount(20m, "parcial", Guid.NewGuid()); // effective 30
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);

        db.GroupTransactions.Add(new GroupTransactionEntity(
            seed.Group.Id, 50m, "C – P", DateOnly.FromDateTime(DateTime.UtcNow),
            TransactionSourceType.ExtraCharge, payment.Id, "P"));
        await db.SaveChangesAsync();

        var result = await MakeSut(db).SyncPaidPaymentsAsync(seed.Group.Id);

        result.Success.Should().BeTrue();
        result.Data!.Updated.Should().Be(1);

        var tx = await db.GroupTransactions.SingleAsync();
        tx.Amount.Should().Be(30m);
        tx.SourceId.Should().Be(payment.Id);
    }

    [Fact]
    public async Task Sync_CancelledCharge_IsIgnored()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_CancelledCharge_IsIgnored));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 45m, null, Guid.NewGuid());
        charge.Cancel();
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 45m);
        payment.MarkAsPaid(null, null, null, null);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).SyncPaidPaymentsAsync(seed.Group.Id);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().Be(0);
        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    // ── ClearAllTransactionsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ClearAllTransactions_EmptyCaixa_ReturnsZero()
    {
        await using var db = DbContextFactory.Create(nameof(ClearAllTransactions_EmptyCaixa_ReturnsZero));

        var result = await MakeSut(db).ClearAllTransactionsAsync(Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data.Should().Be(0);
        result.Message.Should().Contain("vazio");
    }

    [Fact]
    public async Task ClearAllTransactions_RemovesOnlyGroupRows()
    {
        await using var db = DbContextFactory.Create(nameof(ClearAllTransactions_RemovesOnlyGroupRows));
        var groupId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        db.GroupTransactions.AddRange(
            new GroupTransactionEntity(groupId, TransactionType.Income, 10m, "a", new DateOnly(2026, 5, 1), userId),
            new GroupTransactionEntity(groupId, TransactionType.Income, 20m, "b", new DateOnly(2026, 5, 2), userId),
            new GroupTransactionEntity(otherId, TransactionType.Income, 30m, "c", new DateOnly(2026, 5, 3), userId));
        await db.SaveChangesAsync();

        var result = await MakeSut(db).ClearAllTransactionsAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(2);
        (await db.GroupTransactions.CountAsync()).Should().Be(1, "transações de outros grupos permanecem");
    }
}
