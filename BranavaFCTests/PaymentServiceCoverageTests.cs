using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Payments;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class PaymentServiceCoverageTests
{
    private static PaymentService MakeSut(
        AppDbContext db,
        IFinancialTransactionService? tx = null,
        IPushService? push = null) =>
        new(db,
            push ?? Mock.Of<IPushService>(),
            Mock.Of<ILogger<PaymentService>>(),
            tx ?? Mock.Of<IFinancialTransactionService>());

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

    // ── GetMonthlyGridAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMonthlyGrid_NoPlayers_ReturnsEmptyGrid()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyGrid_NoPlayers_ReturnsEmptyGrid));
        var sut = MakeSut(db);

        var result = await sut.GetMonthlyGridAsync(Guid.NewGuid(), 2026);

        result.Success.Should().BeTrue();
        result.Data!.Year.Should().Be(2026);
        result.Data.Players.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMonthlyGrid_ExistingRecord_MapsDiscountAndProof()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyGrid_ExistingRecord_MapsDiscountAndProof));
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);
        var today = DateTime.UtcNow;

        var record = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, today.Year, today.Month, 100m);
        record.ApplyDiscount(10m, "motivo", Guid.NewGuid());
        record.MarkAsPaid(Guid.NewGuid(), "b64", "proof.png", "image/png");
        db.MonthlyPayments.Add(record);
        await db.SaveChangesAsync();

        var result = await sut(db).GetMonthlyGridAsync(seed.Group.Id, today.Year);

        result.Success.Should().BeTrue();
        var row  = result.Data!.Players.Single();
        var cell = row.Months.Single(m => m.Month == today.Month);
        cell.Status.Should().Be(PaymentStatus.Paid);
        cell.Discount.Should().Be(10m);
        cell.DiscountReason.Should().Be("motivo");
        cell.HasProof.Should().BeTrue();
        cell.ProofFileName.Should().Be("proof.png");

        static PaymentService sut(AppDbContext db) => MakeSut(db);
    }

    [Fact]
    public async Task GetMonthlyGrid_PastYear_MemberSinceBefore_Returns12Months()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyGrid_PastYear_MemberSinceBefore_Returns12Months));
        var year = DateTime.UtcNow.Year - 1;
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 50m,
            joinedAt: new DateTime(year - 1, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await MakeSut(db).GetMonthlyGridAsync(seed.Group.Id, year);

        result.Success.Should().BeTrue();
        var row = result.Data!.Players.Single();
        row.Months.Should().HaveCount(12);
        row.Months.Should().AllSatisfy(m => m.Status.Should().Be(PaymentStatus.Pending));
    }

    [Fact]
    public async Task GetMonthlyGrid_PastYear_PlayerJoinedAfter_ReturnsNoMonths()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyGrid_PastYear_PlayerJoinedAfter_ReturnsNoMonths));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 50m); // joined now

        var result = await MakeSut(db).GetMonthlyGridAsync(seed.Group.Id, DateTime.UtcNow.Year - 1);

        result.Success.Should().BeTrue();
        result.Data!.Players.Single().Months.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMonthlyGrid_NoSettings_CellsHaveZeroAmount()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyGrid_NoSettings_CellsHaveZeroAmount));
        var seed = await SeedGroupWithPlayerAsync(db); // no settings

        var result = await MakeSut(db).GetMonthlyGridAsync(seed.Group.Id, DateTime.UtcNow.Year);

        result.Success.Should().BeTrue();
        result.Data!.MonthlyFee.Should().BeNull();
        result.Data.Players.Single().Months.Should().AllSatisfy(m => m.Amount.Should().Be(0m));
    }

    // ── InitiateMonthlyAsync / IsMonthInitiatedAsync ──────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task InitiateMonthly_InvalidMonth_ReturnsBadRequest(int month)
    {
        await using var db = DbContextFactory.Create(nameof(InitiateMonthly_InvalidMonth_ReturnsBadRequest) + month);
        var result = await MakeSut(db).InitiateMonthlyAsync(Guid.NewGuid(), 2026, month);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task InitiateMonthly_NoPlayers_ReturnsZeroZero()
    {
        await using var db = DbContextFactory.Create(nameof(InitiateMonthly_NoPlayers_ReturnsZeroZero));
        var result = await MakeSut(db).InitiateMonthlyAsync(Guid.NewGuid(), 2026, 5);

        result.Success.Should().BeTrue();
        result.Data.Should().Be((0, 0));
    }

    [Fact]
    public async Task InitiateMonthly_SecondCall_SkipsExisting()
    {
        await using var db = DbContextFactory.Create(nameof(InitiateMonthly_SecondCall_SkipsExisting));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);
        var sut  = MakeSut(db);

        var first  = await sut.InitiateMonthlyAsync(seed.Group.Id, 2026, 5);
        var second = await sut.InitiateMonthlyAsync(seed.Group.Id, 2026, 5);

        first.Data.Created.Should().Be(1);
        first.Data.Skipped.Should().Be(0);
        second.Data.Created.Should().Be(0);
        second.Data.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task InitiateMonthly_NoSettings_UsesZeroFee()
    {
        await using var db = DbContextFactory.Create(nameof(InitiateMonthly_NoSettings_UsesZeroFee));
        var seed = await SeedGroupWithPlayerAsync(db); // no settings

        var result = await MakeSut(db).InitiateMonthlyAsync(seed.Group.Id, 2026, 4);

        result.Success.Should().BeTrue();
        (await db.MonthlyPayments.SingleAsync()).Amount.Should().Be(0m);
    }

    [Fact]
    public async Task IsMonthInitiated_ReflectsRecords()
    {
        await using var db = DbContextFactory.Create(nameof(IsMonthInitiated_ReflectsRecords));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);
        var sut  = MakeSut(db);

        (await sut.IsMonthInitiatedAsync(seed.Group.Id, 2026, 5)).Data.Should().BeFalse();

        await sut.InitiateMonthlyAsync(seed.Group.Id, 2026, 5);

        (await sut.IsMonthInitiatedAsync(seed.Group.Id, 2026, 5)).Data.Should().BeTrue();
    }

    // ── UpsertMonthlyPaymentAsync ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertMonthly_NonAdmin_NotOwner_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertMonthly_NonAdmin_NotOwner_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);

        var dto = new UpsertMonthlyPaymentDto
        {
            PlayerId = seed.Player.Id, Year = 2026, Month = 5, Status = PaymentStatus.Paid,
        };

        var result = await MakeSut(db).UpsertMonthlyPaymentAsync(
            seed.Group.Id, dto, actingUserId: Guid.NewGuid(), isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task UpsertMonthly_NonAdmin_WithDiscount_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertMonthly_NonAdmin_WithDiscount_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);

        var dto = new UpsertMonthlyPaymentDto
        {
            PlayerId = seed.Player.Id, Year = 2026, Month = 5,
            Status = PaymentStatus.Paid, Discount = 10m,
        };

        var result = await MakeSut(db).UpsertMonthlyPaymentAsync(
            seed.Group.Id, dto, actingUserId: seed.User.Id, isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task UpsertMonthly_NonAdminOwner_PaidWithProof_CreatesRecord()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertMonthly_NonAdminOwner_PaidWithProof_CreatesRecord));
        var seed   = await SeedGroupWithPlayerAsync(db, monthlyFee: 80m);
        var txMock = new Mock<IFinancialTransactionService>();
        var sut    = MakeSut(db, txMock.Object);
        var today  = DateTime.UtcNow;

        var dto = new UpsertMonthlyPaymentDto
        {
            PlayerId = seed.Player.Id, Year = today.Year, Month = today.Month,
            Status = PaymentStatus.Paid,
            ProofBase64 = "b64", ProofFileName = "p.png", ProofMimeType = "image/png",
        };

        var result = await sut.UpsertMonthlyPaymentAsync(
            seed.Group.Id, dto, actingUserId: seed.User.Id, isAdmin: false);

        result.Success.Should().BeTrue();

        var record = await db.MonthlyPayments.SingleAsync();
        record.Amount.Should().Be(80m);
        record.Status.Should().Be(PaymentStatus.Paid);
        record.MarkedByAdminId.Should().BeNull("non-admin self payment");
        record.ProofBase64.Should().Be("b64");

        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            seed.Group.Id, TransactionSourceType.MonthlyPayment, record.Id,
            80m, It.IsAny<string>(), It.IsAny<DateOnly>(),
            true, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertMonthly_AdminFullDiscount_CaixaUsesEffectiveBefore()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertMonthly_AdminFullDiscount_CaixaUsesEffectiveBefore));
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);
        var today = DateTime.UtcNow;

        var record = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, today.Year, today.Month, 100m);
        db.MonthlyPayments.Add(record);
        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        var sut    = MakeSut(db, txMock.Object);
        var admin  = Guid.NewGuid();

        var dto = new UpsertMonthlyPaymentDto
        {
            PlayerId = seed.Player.Id, Year = today.Year, Month = today.Month,
            Status = PaymentStatus.Paid, Discount = 100m, DiscountReason = "cortesia",
        };

        var result = await sut.UpsertMonthlyPaymentAsync(seed.Group.Id, dto, admin, isAdmin: true);

        result.Success.Should().BeTrue();
        record.Discount.Should().Be(100m);
        record.Status.Should().Be(PaymentStatus.Paid);

        // Desconto integral zera o effective — caixa deve usar o valor anterior (100)
        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            seed.Group.Id, TransactionSourceType.MonthlyPayment, record.Id,
            100m, It.IsAny<string>(), It.IsAny<DateOnly>(),
            true, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertMonthly_MarkPendingPreviouslyPaid_NotifiesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertMonthly_MarkPendingPreviouslyPaid_NotifiesPlayer));
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);
        var today = DateTime.UtcNow;

        var record = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, today.Year, today.Month, 100m);
        record.MarkAsPaid(Guid.NewGuid(), null, null, null);
        db.MonthlyPayments.Add(record);
        await db.SaveChangesAsync();

        var pushMock = new Mock<IPushService>();
        var sut      = MakeSut(db, push: pushMock.Object);

        var dto = new UpsertMonthlyPaymentDto
        {
            PlayerId = seed.Player.Id, Year = today.Year, Month = today.Month,
            Status = PaymentStatus.Pending,
        };

        var result = await sut.UpsertMonthlyPaymentAsync(seed.Group.Id, dto, Guid.NewGuid(), isAdmin: true);

        result.Success.Should().BeTrue();
        record.Status.Should().Be(PaymentStatus.Pending);
        record.PaidAt.Should().BeNull();

        pushMock.Verify(p => p.SendToUserAsync(
            seed.User.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(),
            seed.Group.Id), Times.Once);
    }

    [Fact]
    public async Task UpsertMonthly_NewRecordForGoalkeeper_UsesGoalkeeperFee()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertMonthly_NewRecordForGoalkeeper_UsesGoalkeeperFee));
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m, goalkeeperFee: 60m, goalkeeper: true);
        var today = DateTime.UtcNow;

        var dto = new UpsertMonthlyPaymentDto
        {
            PlayerId = seed.Player.Id, Year = today.Year, Month = today.Month,
            Status = PaymentStatus.Paid,
        };

        var result = await MakeSut(db).UpsertMonthlyPaymentAsync(seed.Group.Id, dto, Guid.NewGuid(), isAdmin: true);

        result.Success.Should().BeTrue();
        (await db.MonthlyPayments.SingleAsync()).Amount.Should().Be(60m);
    }

    // ── CreateExtraChargeAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CreateExtraCharge_EmptyPlayerIds_BadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(CreateExtraCharge_EmptyPlayerIds_BadRequest));
        var dto = new CreateExtraChargeDto { Name = "N", Amount = 10m, PlayerIds = [] };

        var result = await MakeSut(db).CreateExtraChargeAsync(Guid.NewGuid(), dto, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task CreateExtraCharge_NoValidPlayers_BadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(CreateExtraCharge_NoValidPlayers_BadRequest));
        var dto = new CreateExtraChargeDto { Name = "N", Amount = 10m, PlayerIds = [Guid.NewGuid()] };

        var result = await MakeSut(db).CreateExtraChargeAsync(Guid.NewGuid(), dto, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Nenhum jogador");
    }

    [Fact]
    public async Task CreateExtraCharge_HappyPath_CreatesChargeAndPayments()
    {
        await using var db = DbContextFactory.Create(nameof(CreateExtraCharge_HappyPath_CreatesChargeAndPayments));
        var seed = await SeedGroupWithPlayerAsync(db);
        var dto  = new CreateExtraChargeDto
        {
            Name = "Churrasco", Description = "fim de ano", Amount = 40m,
            DueDate = new DateOnly(2026, 12, 20), PlayerIds = [seed.Player.Id],
        };

        var result = await MakeSut(db).CreateExtraChargeAsync(seed.Group.Id, dto, Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data!.Name.Should().Be("Churrasco");
        result.Data.Payments.Should().ContainSingle(p => p.PlayerId == seed.Player.Id && p.PlayerName == "P");

        (await db.ExtraCharges.CountAsync()).Should().Be(1);
        (await db.ExtraChargePayments.CountAsync()).Should().Be(1);
    }

    // ── Cancel / Reactivate ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelExtraCharge_NotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(CancelExtraCharge_NotFound_ReturnsNotFound));
        var result = await MakeSut(db).CancelExtraChargeAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task CancelExtraCharge_HappyPath_SetsCancelled()
    {
        await using var db = DbContextFactory.Create(nameof(CancelExtraCharge_HappyPath_SetsCancelled));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var charge = new ExtraChargeEntity(seed.Group.Id, "C", null, 10m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).CancelExtraChargeAsync(seed.Group.Id, charge.Id);

        result.Success.Should().BeTrue();
        charge.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ReactivateExtraCharge_NotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateExtraCharge_NotFound_ReturnsNotFound));
        var result = await MakeSut(db).ReactivateExtraChargeAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ReactivateExtraCharge_NotCancelled_BadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateExtraCharge_NotCancelled_BadRequest));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var charge = new ExtraChargeEntity(seed.Group.Id, "C", null, 10m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).ReactivateExtraChargeAsync(seed.Group.Id, charge.Id);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task ReactivateExtraCharge_HappyPath_ClearsCancelled()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateExtraCharge_HappyPath_ClearsCancelled));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var charge = new ExtraChargeEntity(seed.Group.Id, "C", null, 10m, null, Guid.NewGuid());
        charge.Cancel();
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 10m));
        await db.SaveChangesAsync();

        var result = await MakeSut(db).ReactivateExtraChargeAsync(seed.Group.Id, charge.Id);

        result.Success.Should().BeTrue();
        result.Data!.IsCancelled.Should().BeFalse();
        charge.IsCancelled.Should().BeFalse();
    }

    // ── UpdateExtraChargeDetailsAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateExtraChargeDetails_NotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateExtraChargeDetails_NotFound_ReturnsNotFound));
        var result = await MakeSut(db).UpdateExtraChargeDetailsAsync(
            Guid.NewGuid(), Guid.NewGuid(), new UpdateExtraChargeDetailsDto());

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateExtraChargeDetails_InvalidAmount_BadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateExtraChargeDetails_InvalidAmount_BadRequest));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var charge = new ExtraChargeEntity(seed.Group.Id, "C", null, 10m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).UpdateExtraChargeDetailsAsync(
            seed.Group.Id, charge.Id, new UpdateExtraChargeDetailsDto { Amount = 0m });

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        charge.Amount.Should().Be(10m, "valor inválido não deve ser aplicado");
    }

    [Fact]
    public async Task UpdateExtraChargeDetails_HappyPath_UpdatesFields()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateExtraChargeDetails_HappyPath_UpdatesFields));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var charge = new ExtraChargeEntity(seed.Group.Id, "Velho", "desc", 10m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 10m));
        await db.SaveChangesAsync();

        var result = await MakeSut(db).UpdateExtraChargeDetailsAsync(
            seed.Group.Id, charge.Id,
            new UpdateExtraChargeDetailsDto { Name = "Novo", Description = "", Amount = 25m });

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Novo");
        result.Data.Description.Should().BeNull("descrição vazia limpa o campo");
        result.Data.Amount.Should().Be(25m);
    }

    // ── BulkDiscountExtraChargeAsync ──────────────────────────────────────────

    [Fact]
    public async Task BulkDiscount_NegativeDiscount_BadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(BulkDiscount_NegativeDiscount_BadRequest));
        var dto = new BulkExtraChargeDiscountDto { Discount = -1m, PlayerIds = [Guid.NewGuid()] };

        var result = await MakeSut(db).BulkDiscountExtraChargeAsync(
            Guid.NewGuid(), Guid.NewGuid(), dto, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task BulkDiscount_EmptyPlayerIds_ReturnsOkWithoutWork()
    {
        await using var db = DbContextFactory.Create(nameof(BulkDiscount_EmptyPlayerIds_ReturnsOkWithoutWork));
        var txMock = new Mock<IFinancialTransactionService>();
        var dto    = new BulkExtraChargeDiscountDto { Discount = 10m, PlayerIds = [] };

        var result = await MakeSut(db, txMock.Object).BulkDiscountExtraChargeAsync(
            Guid.NewGuid(), Guid.NewGuid(), dto, Guid.NewGuid());

        result.Success.Should().BeTrue();
        txMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BulkDiscount_FullDiscount_AutoPays_CaixaUsesEffectiveBefore()
    {
        await using var db = DbContextFactory.Create(nameof(BulkDiscount_FullDiscount_AutoPays_CaixaUsesEffectiveBefore));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 50m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 50m);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        var dto    = new BulkExtraChargeDiscountDto
        {
            Discount = 50m, DiscountReason = "cortesia", PlayerIds = [seed.Player.Id],
        };

        var result = await MakeSut(db, txMock.Object).BulkDiscountExtraChargeAsync(
            seed.Group.Id, charge.Id, dto, Guid.NewGuid());

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid, "desconto integral auto-paga");
        payment.Discount.Should().Be(50m);

        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            seed.Group.Id, TransactionSourceType.ExtraCharge, payment.Id,
            50m, It.IsAny<string>(), It.IsAny<DateOnly>(),
            true, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDiscount_PartialWithMarkAsPaid_PaysAndUsesEffectiveAfter()
    {
        await using var db = DbContextFactory.Create(nameof(BulkDiscount_PartialWithMarkAsPaid_PaysAndUsesEffectiveAfter));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 50m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 50m);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        var dto    = new BulkExtraChargeDiscountDto
        {
            Discount = 20m, PlayerIds = [seed.Player.Id], MarkAsPaid = true,
        };

        var result = await MakeSut(db, txMock.Object).BulkDiscountExtraChargeAsync(
            seed.Group.Id, charge.Id, dto, Guid.NewGuid());

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);

        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            seed.Group.Id, TransactionSourceType.ExtraCharge, payment.Id,
            30m, It.IsAny<string>(), It.IsAny<DateOnly>(),
            true, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDiscount_PartialWithoutMarkAsPaid_StaysPending()
    {
        await using var db = DbContextFactory.Create(nameof(BulkDiscount_PartialWithoutMarkAsPaid_StaysPending));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 50m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 50m);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        var dto    = new BulkExtraChargeDiscountDto { Discount = 20m, PlayerIds = [seed.Player.Id] };

        var result = await MakeSut(db, txMock.Object).BulkDiscountExtraChargeAsync(
            seed.Group.Id, charge.Id, dto, Guid.NewGuid());

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Pending);

        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            seed.Group.Id, TransactionSourceType.ExtraCharge, payment.Id,
            30m, It.IsAny<string>(), It.IsAny<DateOnly>(),
            false, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpsertExtraChargePaymentAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpsertExtraPayment_NonAdmin_NotOwner_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertExtraPayment_NonAdmin_NotOwner_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).UpsertExtraChargePaymentAsync(
            seed.Group.Id, Guid.NewGuid(), seed.Player.Id,
            new UpsertExtraChargePaymentDto { Status = PaymentStatus.Paid },
            actingUserId: Guid.NewGuid(), isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task UpsertExtraPayment_NonAdmin_WithDiscount_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertExtraPayment_NonAdmin_WithDiscount_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).UpsertExtraChargePaymentAsync(
            seed.Group.Id, Guid.NewGuid(), seed.Player.Id,
            new UpsertExtraChargePaymentDto { Status = PaymentStatus.Paid, Discount = 5m },
            actingUserId: seed.User.Id, isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task UpsertExtraPayment_PaymentNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertExtraPayment_PaymentNotFound_ReturnsNotFound));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).UpsertExtraChargePaymentAsync(
            seed.Group.Id, Guid.NewGuid(), seed.Player.Id,
            new UpsertExtraChargePaymentDto { Status = PaymentStatus.Paid },
            actingUserId: Guid.NewGuid(), isAdmin: true);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpsertExtraPayment_AdminFullDiscount_CaixaUsesEffectiveBefore()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertExtraPayment_AdminFullDiscount_CaixaUsesEffectiveBefore));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 60m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 60m);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();

        var result = await MakeSut(db, txMock.Object).UpsertExtraChargePaymentAsync(
            seed.Group.Id, charge.Id, seed.Player.Id,
            new UpsertExtraChargePaymentDto { Status = PaymentStatus.Paid, Discount = 60m },
            actingUserId: Guid.NewGuid(), isAdmin: true);

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);

        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            seed.Group.Id, TransactionSourceType.ExtraCharge, payment.Id,
            60m, It.IsAny<string>(), It.IsAny<DateOnly>(),
            true, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertExtraPayment_NonAdminOwner_PaidWithProof_Succeeds()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertExtraPayment_NonAdminOwner_PaidWithProof_Succeeds));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 60m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 60m);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var pushMock = new Mock<IPushService>();

        var result = await MakeSut(db, push: pushMock.Object).UpsertExtraChargePaymentAsync(
            seed.Group.Id, charge.Id, seed.Player.Id,
            new UpsertExtraChargePaymentDto
            {
                Status = PaymentStatus.Paid,
                ProofBase64 = "b64", ProofFileName = "p.png", ProofMimeType = "image/png",
            },
            actingUserId: seed.User.Id, isAdmin: false);

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.ProofBase64.Should().Be("b64");
        payment.MarkedByAdminId.Should().BeNull();

        // Notifica financeiros do pagamento + meta atingida (único pagamento da cobrança)
        pushMock.Verify(p => p.SendToGroupFinanceirosAsync(
            seed.Group.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UpsertExtraPayment_MarkPendingPreviouslyPaid_NotifiesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertExtraPayment_MarkPendingPreviouslyPaid_NotifiesPlayer));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 60m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 60m);
        payment.MarkAsPaid(Guid.NewGuid(), null, null, null);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var pushMock = new Mock<IPushService>();

        var result = await MakeSut(db, push: pushMock.Object).UpsertExtraChargePaymentAsync(
            seed.Group.Id, charge.Id, seed.Player.Id,
            new UpsertExtraChargePaymentDto { Status = PaymentStatus.Pending },
            actingUserId: Guid.NewGuid(), isAdmin: true);

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Pending);

        pushMock.Verify(p => p.SendToUserAsync(
            seed.User.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(),
            seed.Group.Id), Times.Once);
    }

    // ── GetMyMonthlyRowAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMyMonthlyRow_NoPlayer_ReturnsNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyMonthlyRow_NoPlayer_ReturnsNull));

        var result = await MakeSut(db).GetMyMonthlyRowAsync(Guid.NewGuid(), Guid.NewGuid(), 2026);

        result.Success.Should().BeTrue();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetMyMonthlyRow_WithRecord_MapsCells()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyMonthlyRow_WithRecord_MapsCells));
        var year = DateTime.UtcNow.Year;
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m,
            joinedAt: new DateTime(year - 1, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var record = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, year, 1, 100m);
        record.MarkAsPaid(Guid.NewGuid(), "b64", "p.png", "image/png");
        db.MonthlyPayments.Add(record);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetMyMonthlyRowAsync(seed.Group.Id, seed.User.Id, year);

        result.Success.Should().BeTrue();
        var row = result.Data!;
        row.PlayerId.Should().Be(seed.Player.Id);
        row.Months.Should().HaveCount(DateTime.UtcNow.Month, "membro desde ano anterior → meses até o atual");

        var jan = row.Months.Single(m => m.Month == 1);
        jan.Status.Should().Be(PaymentStatus.Paid);
        jan.HasProof.Should().BeTrue();

        var others = row.Months.Where(m => m.Month != 1);
        others.Should().AllSatisfy(m =>
        {
            m.Status.Should().Be(PaymentStatus.Pending);
            m.Amount.Should().Be(100m);
        });
    }

    // ── GetPaymentSummaryAsync / GetMySummaryAsync ────────────────────────────

    [Fact]
    public async Task GetPaymentSummary_NonAdmin_NotOwner_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(GetPaymentSummary_NonAdmin_NotOwner_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).GetPaymentSummaryAsync(
            seed.Group.Id, seed.Player.Id, requestingUserId: Guid.NewGuid(), isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task GetPaymentSummary_UnknownPlayer_ReturnsNoMonthlyPending()
    {
        await using var db = DbContextFactory.Create(nameof(GetPaymentSummary_UnknownPlayer_ReturnsNoMonthlyPending));

        var result = await MakeSut(db).GetPaymentSummaryAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data!.HasPendingMonthly.Should().BeFalse();
        result.Data.PendingExtraCharges.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPaymentSummary_CountsPendingMonthsAndExtras()
    {
        await using var db = DbContextFactory.Create(nameof(GetPaymentSummary_CountsPendingMonthsAndExtras));
        var today = DateTime.UtcNow;
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m,
            joinedAt: new DateTime(today.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 50m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 50m);
        payment.ApplyDiscount(10m, "parcial", Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetPaymentSummaryAsync(seed.Group.Id, seed.Player.Id);

        result.Success.Should().BeTrue();
        result.Data!.HasPendingMonthly.Should().BeTrue();
        result.Data.PendingMonthsCount.Should().Be(today.Month, "nenhum mês pago no ano corrente");

        var extra = result.Data.PendingExtraCharges.Single();
        extra.ChargeId.Should().Be(charge.Id);
        extra.FinalAmount.Should().Be(40m);
    }

    [Fact]
    public async Task GetMySummary_NoPlayer_ReturnsEmptySummary()
    {
        await using var db = DbContextFactory.Create(nameof(GetMySummary_NoPlayer_ReturnsEmptySummary));

        var result = await MakeSut(db).GetMySummaryAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data!.HasPendingMonthly.Should().BeFalse();
    }

    [Fact]
    public async Task GetMySummary_WithPlayer_DelegatesToSummary()
    {
        await using var db = DbContextFactory.Create(nameof(GetMySummary_WithPlayer_DelegatesToSummary));
        var seed = await SeedGroupWithPlayerAsync(db, monthlyFee: 100m);

        var result = await MakeSut(db).GetMySummaryAsync(seed.Group.Id, seed.User.Id);

        result.Success.Should().BeTrue();
        result.Data!.HasPendingMonthly.Should().BeTrue("mês atual sem pagamento");
    }

    // ── Comprovantes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMonthlyProof_NonAdmin_NotOwner_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyProof_NonAdmin_NotOwner_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).GetMonthlyProofAsync(
            seed.Group.Id, seed.Player.Id, 2026, 5,
            requestingUserId: Guid.NewGuid(), isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task GetMonthlyProof_NoRecordOrNoProof_NotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyProof_NoRecordOrNoProof_NotFound));
        var seed = await SeedGroupWithPlayerAsync(db);

        // Sem registro
        var missing = await MakeSut(db).GetMonthlyProofAsync(seed.Group.Id, seed.Player.Id, 2026, 5);
        missing.Status.Should().Be(ResultStatus.NotFound);

        // Registro sem comprovante
        db.MonthlyPayments.Add(new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, 2026, 5, 100m));
        await db.SaveChangesAsync();

        var noProof = await MakeSut(db).GetMonthlyProofAsync(seed.Group.Id, seed.Player.Id, 2026, 5);
        noProof.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetMonthlyProof_WithProof_ReturnsDefaultsWhenMetadataMissing()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlyProof_WithProof_ReturnsDefaultsWhenMetadataMissing));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var record = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, 2026, 5, 100m);
        record.MarkAsPaid(null, "b64", null, null);
        db.MonthlyPayments.Add(record);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetMonthlyProofAsync(
            seed.Group.Id, seed.Player.Id, 2026, 5,
            requestingUserId: seed.User.Id, isAdmin: false);

        result.Success.Should().BeTrue();
        result.Data!.Base64.Should().Be("b64");
        result.Data.FileName.Should().Be("comprovante");
        result.Data.MimeType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task GetExtraChargeProof_NonAdmin_NotOwner_Forbidden()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargeProof_NonAdmin_NotOwner_Forbidden));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).GetExtraChargeProofAsync(
            seed.Group.Id, Guid.NewGuid(), seed.Player.Id,
            requestingUserId: Guid.NewGuid(), isAdmin: false);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task GetExtraChargeProof_NoProof_NotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargeProof_NoProof_NotFound));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 10m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 10m));
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetExtraChargeProofAsync(seed.Group.Id, charge.Id, seed.Player.Id);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetExtraChargeProof_WithProof_ReturnsData()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargeProof_WithProof_ReturnsData));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 10m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 10m);
        payment.MarkAsPaid(null, "b64", "recibo.pdf", "application/pdf");
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetExtraChargeProofAsync(seed.Group.Id, charge.Id, seed.Player.Id);

        result.Success.Should().BeTrue();
        result.Data!.Base64.Should().Be("b64");
        result.Data.FileName.Should().Be("recibo.pdf");
        result.Data.MimeType.Should().Be("application/pdf");
    }

    // ── GetMyPendingItemsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetMyPendingItems_NoPlayer_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyPendingItems_NoPlayer_ReturnsEmpty));

        var result = await MakeSut(db).GetMyPendingItemsAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyPendingItems_FullyDiscountedPaidExtra_IsSkipped()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyPendingItems_FullyDiscountedPaidExtra_IsSkipped));
        var seed    = await SeedGroupWithPlayerAsync(db); // sem settings, sem mensalidades
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 50m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 50m);
        payment.ApplyDiscount(50m, "total", Guid.NewGuid()); // auto-paid, effective 0
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetMyPendingItemsAsync(seed.Group.Id, seed.User.Id);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty("sem fee/registros mensais e extra auto-pago com desconto total");
    }

    [Fact]
    public async Task GetMyPendingItems_MonthlyRecordsWithoutFee_UsesRecordAmounts()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyPendingItems_MonthlyRecordsWithoutFee_UsesRecordAmounts));
        var today = DateTime.UtcNow;
        var seed  = await SeedGroupWithPlayerAsync(db); // sem settings → fee 0

        var record = new MonthlyPaymentEntity(seed.Group.Id, seed.Player.Id, today.Year, today.Month, 80m);
        record.ApplyDiscount(30m, "parcial", Guid.NewGuid());
        db.MonthlyPayments.Add(record);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetMyPendingItemsAsync(seed.Group.Id, seed.User.Id);

        result.Success.Should().BeTrue();
        var item = result.Data!.Single(i => i.Type == PendingPaymentType.Monthly && i.Month == today.Month);
        item.Amount.Should().Be(80m);
        item.Discount.Should().Be(30m);
        item.FinalAmount.Should().Be(50m);
        item.IsPaid.Should().BeFalse();
    }

    // ── PaySelectedAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task PaySelected_EmptyItems_BadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(PaySelected_EmptyItems_BadRequest));

        var result = await MakeSut(db).PaySelectedAsync(
            Guid.NewGuid(), Guid.NewGuid(), new PaySelectedDto { Items = [] });

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task PaySelected_PlayerNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(PaySelected_PlayerNotFound_ReturnsNotFound));

        var dto = new PaySelectedDto
        {
            Items = [new PaySelectedItem { Type = PendingPaymentType.Monthly, Year = 2026, Month = 5, IsPaid = true }],
        };

        var result = await MakeSut(db).PaySelectedAsync(Guid.NewGuid(), Guid.NewGuid(), dto);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task PaySelected_MonthlyMissingRecord_IsPaidFalse_SkipsCreation()
    {
        await using var db = DbContextFactory.Create(nameof(PaySelected_MonthlyMissingRecord_IsPaidFalse_SkipsCreation));
        var seed   = await SeedGroupWithPlayerAsync(db, monthlyFee: 90m);
        var txMock = new Mock<IFinancialTransactionService>();
        var today  = DateTime.UtcNow;

        var dto = new PaySelectedDto
        {
            Items = [new PaySelectedItem { Type = PendingPaymentType.Monthly, Year = today.Year, Month = today.Month, IsPaid = false }],
        };

        var result = await MakeSut(db, txMock.Object).PaySelectedAsync(seed.Group.Id, seed.User.Id, dto);

        result.Success.Should().BeTrue();
        (await db.MonthlyPayments.CountAsync()).Should().Be(0, "não cria registro para desmarcar mês inexistente");
        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            It.IsAny<Guid>(), It.IsAny<TransactionSourceType>(), It.IsAny<Guid>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PaySelected_MonthlyMissingRecord_IsPaidTrue_CreatesAndPays()
    {
        await using var db = DbContextFactory.Create(nameof(PaySelected_MonthlyMissingRecord_IsPaidTrue_CreatesAndPays));
        var seed  = await SeedGroupWithPlayerAsync(db, monthlyFee: 90m);
        var today = DateTime.UtcNow;

        var dto = new PaySelectedDto
        {
            Items = [new PaySelectedItem { Type = PendingPaymentType.Monthly, Year = today.Year, Month = today.Month, IsPaid = true }],
        };

        var result = await MakeSut(db).PaySelectedAsync(seed.Group.Id, seed.User.Id, dto);

        result.Success.Should().BeTrue();
        var record = await db.MonthlyPayments.SingleAsync();
        record.Amount.Should().Be(90m);
        record.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task PaySelected_ExtraPaymentMissing_IsSkipped()
    {
        await using var db = DbContextFactory.Create(nameof(PaySelected_ExtraPaymentMissing_IsSkipped));
        var seed   = await SeedGroupWithPlayerAsync(db);
        var txMock = new Mock<IFinancialTransactionService>();

        var dto = new PaySelectedDto
        {
            Items = [new PaySelectedItem { Type = PendingPaymentType.Extra, ChargeId = Guid.NewGuid(), IsPaid = true }],
        };

        var result = await MakeSut(db, txMock.Object).PaySelectedAsync(seed.Group.Id, seed.User.Id, dto);

        result.Success.Should().BeTrue();
        txMock.Verify(s => s.RecordOrRemovePaymentEntryAsync(
            It.IsAny<Guid>(), It.IsAny<TransactionSourceType>(), It.IsAny<Guid>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PaySelected_UnpayPaidExtra_MarksPendingAndNotifiesPlayer()
    {
        await using var db = DbContextFactory.Create(nameof(PaySelected_UnpayPaidExtra_MarksPendingAndNotifiesPlayer));
        var seed    = await SeedGroupWithPlayerAsync(db);
        var charge  = new ExtraChargeEntity(seed.Group.Id, "C", null, 55m, null, Guid.NewGuid());
        var payment = new ExtraChargePaymentEntity(charge.Id, seed.Player.Id, seed.Group.Id, 55m);
        payment.MarkAsPaid(null, null, null, null);
        db.ExtraCharges.Add(charge);
        db.ExtraChargePayments.Add(payment);
        await db.SaveChangesAsync();

        var pushMock = new Mock<IPushService>();

        var dto = new PaySelectedDto
        {
            Items = [new PaySelectedItem { Type = PendingPaymentType.Extra, ChargeId = charge.Id, IsPaid = false }],
        };

        var result = await MakeSut(db, push: pushMock.Object).PaySelectedAsync(seed.Group.Id, seed.User.Id, dto);

        result.Success.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Pending);

        pushMock.Verify(p => p.SendToUserAsync(
            seed.User.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(),
            seed.Group.Id), Times.Once);
    }

    // ── GetExtraChargesSummaryAsync — visão do próprio jogador ────────────────

    [Fact]
    public async Task GetExtraChargesSummary_UserWithoutPlayer_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargesSummary_UserWithoutPlayer_ReturnsEmpty));
        var seed = await SeedGroupWithPlayerAsync(db);

        var result = await MakeSut(db).GetExtraChargesSummaryAsync(
            seed.Group.Id, DateTime.UtcNow.Year, userId: Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExtraChargesSummary_UserView_FiltersToOwnChargesAndIgnoresCancelled()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargesSummary_UserView_FiltersToOwnChargesAndIgnoresCancelled));
        var seed = await SeedGroupWithPlayerAsync(db);
        var year = DateTime.UtcNow.Year;

        // Cobrança do jogador — paga
        var mine        = new ExtraChargeEntity(seed.Group.Id, "Minha", null, 20m, null, Guid.NewGuid());
        var minePayment = new ExtraChargePaymentEntity(mine.Id, seed.Player.Id, seed.Group.Id, 20m);
        minePayment.MarkAsPaid(null, null, null, null);

        // Cobrança cancelada do jogador — pendente (deve ser ignorada em AllPaid/HasPending)
        var cancelled        = new ExtraChargeEntity(seed.Group.Id, "Cancelada", null, 30m, null, Guid.NewGuid());
        cancelled.Cancel();
        var cancelledPayment = new ExtraChargePaymentEntity(cancelled.Id, seed.Player.Id, seed.Group.Id, 30m);

        // Cobrança de outro jogador — não deve aparecer na visão do usuário
        var otherPlayer  = new PlayerEntity("Outro", Guid.NewGuid(), seed.Group.Id, 5m, false, false, Status.Active);
        var otherCharge  = new ExtraChargeEntity(seed.Group.Id, "Alheia", null, 15m, null, Guid.NewGuid());
        var otherPayment = new ExtraChargePaymentEntity(otherCharge.Id, otherPlayer.Id, seed.Group.Id, 15m);

        db.Players.Add(otherPlayer);
        db.ExtraCharges.AddRange(mine, cancelled, otherCharge);
        db.ExtraChargePayments.AddRange(minePayment, cancelledPayment, otherPayment);
        await db.SaveChangesAsync();

        var result = await MakeSut(db).GetExtraChargesSummaryAsync(seed.Group.Id, year, userId: seed.User.Id);

        result.Success.Should().BeTrue();
        var month = result.Data!.Single();
        month.Month.Should().Be(DateTime.UtcNow.Month);
        month.Count.Should().Be(2, "apenas as duas cobranças em que o jogador está incluído");
        month.AllPaid.Should().BeTrue("cancelada é ignorada; a ativa está paga");
        month.HasPending.Should().BeFalse();
    }
}
