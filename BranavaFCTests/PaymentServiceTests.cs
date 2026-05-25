using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class PaymentServiceTests
{
    private static PaymentService MakeSut(
        BratnavaFC.Infrastructure.Data.AppDbContext db) =>
        new(db, Mock.Of<IPushService>(), Mock.Of<ILogger<PaymentService>>(), Mock.Of<IFinancialTransactionService>());

    // ── GetMonthlyGridAsync — goalkeeper fee ──────────────────────────────────

    [Fact]
    public async Task GetMonthlyGridAsync_WhenGoalkeeperFeeSet_GoalkeeperGetsGoalkeeperFee()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetMonthlyGridAsync_WhenGoalkeeperFeeSet_GoalkeeperGetsGoalkeeperFee));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var userLine = new UserEntity("ul", "F", "L", "line@test.com",  "h", null, null);
        var userGk   = new UserEntity("ug", "G", "K", "gk@test.com",    "h", null, null);
        db.Users.AddRange(userLine, userGk);

        var linePlyr = new PlayerEntity("Line",  userLine.Id, group.Id, 5m, false, false, Status.Active);
        var gkPlyr   = new PlayerEntity("GK",    userGk.Id,   group.Id, 5m, true,  false, Status.Active);
        db.Players.AddRange(linePlyr, gkPlyr);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        settings.SetGoalkeeperMonthlyFee(60m);
        db.GroupSettings.Add(settings);

        await db.SaveChangesAsync();

        var sut  = MakeSut(db);
        var year = DateTime.UtcNow.Year;

        // Act
        var result = await sut.GetMonthlyGridAsync(group.Id, year);

        // Assert
        result.Success.Should().BeTrue();

        var lineRow = result.Data!.Players.First(p => p.PlayerId == linePlyr.Id);
        var gkRow   = result.Data.Players.First(p => p.PlayerId == gkPlyr.Id);

        lineRow.Months.Should().AllSatisfy(m => m.Amount.Should().Be(100m));
        gkRow.Months.Should().AllSatisfy(m => m.Amount.Should().Be(60m));
    }

    [Fact]
    public async Task GetMonthlyGridAsync_WhenGoalkeeperFeeNull_GoalkeeperGetsLineFee()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetMonthlyGridAsync_WhenGoalkeeperFeeNull_GoalkeeperGetsLineFee));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var userGk = new UserEntity("ug", "G", "K", "gk@test.com", "h", null, null);
        db.Users.Add(userGk);

        var gkPlyr = new PlayerEntity("GK", userGk.Id, group.Id, 5m, true, false, Status.Active);
        db.Players.Add(gkPlyr);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        // GoalkeeperMonthlyFee left null → falls back to MonthlyFee
        db.GroupSettings.Add(settings);

        await db.SaveChangesAsync();

        var sut  = MakeSut(db);
        var year = DateTime.UtcNow.Year;

        // Act
        var result = await sut.GetMonthlyGridAsync(group.Id, year);

        // Assert
        result.Success.Should().BeTrue();
        var gkRow = result.Data!.Players.First(p => p.PlayerId == gkPlyr.Id);
        gkRow.Months.Should().AllSatisfy(m => m.Amount.Should().Be(100m));
    }

    [Fact]
    public async Task GetMonthlyGridAsync_ShouldReturnBothFeesInDto()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetMonthlyGridAsync_ShouldReturnBothFeesInDto));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user = new UserEntity("u", "F", "L", "u@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        settings.SetGoalkeeperMonthlyFee(60m);
        db.GroupSettings.Add(settings);

        await db.SaveChangesAsync();

        var sut  = MakeSut(db);
        var year = DateTime.UtcNow.Year;

        // Act
        var result = await sut.GetMonthlyGridAsync(group.Id, year);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MonthlyFee.Should().Be(100m);
        result.Data.GoalkeeperMonthlyFee.Should().Be(60m);
    }

    // ── InitiateMonthlyAsync — goalkeeper fee ─────────────────────────────────

    [Fact]
    public async Task InitiateMonthlyAsync_WhenGoalkeeperFeeSet_GoalkeeperGetsGoalkeeperFee()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(InitiateMonthlyAsync_WhenGoalkeeperFeeSet_GoalkeeperGetsGoalkeeperFee));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var userLine = new UserEntity("ul", "F", "L", "line@test.com", "h", null, null);
        var userGk   = new UserEntity("ug", "G", "K", "gk@test.com",   "h", null, null);
        db.Users.AddRange(userLine, userGk);

        var linePlyr = new PlayerEntity("Line", userLine.Id, group.Id, 5m, false, false, Status.Active);
        var gkPlyr   = new PlayerEntity("GK",   userGk.Id,   group.Id, 5m, true,  false, Status.Active);
        db.Players.AddRange(linePlyr, gkPlyr);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        settings.SetGoalkeeperMonthlyFee(60m);
        db.GroupSettings.Add(settings);

        await db.SaveChangesAsync();

        var sut   = MakeSut(db);
        var today = DateTime.UtcNow;

        // Act
        var result = await sut.InitiateMonthlyAsync(group.Id, today.Year, today.Month);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Created.Should().Be(2);

        var lineRecord = await db.MonthlyPayments.FirstAsync(
            m => m.PlayerId == linePlyr.Id && m.Year == today.Year && m.Month == today.Month);
        var gkRecord = await db.MonthlyPayments.FirstAsync(
            m => m.PlayerId == gkPlyr.Id && m.Year == today.Year && m.Month == today.Month);

        lineRecord.Amount.Should().Be(100m);
        gkRecord.Amount.Should().Be(60m);
    }

    [Fact]
    public async Task InitiateMonthlyAsync_WhenGoalkeeperFeeNull_GoalkeeperGetsLineFee()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(InitiateMonthlyAsync_WhenGoalkeeperFeeNull_GoalkeeperGetsLineFee));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var userGk = new UserEntity("ug", "G", "K", "gk@test.com", "h", null, null);
        db.Users.Add(userGk);

        var gkPlyr = new PlayerEntity("GK", userGk.Id, group.Id, 5m, true, false, Status.Active);
        db.Players.Add(gkPlyr);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        // GoalkeeperMonthlyFee not set → falls back to linePlayerFee
        db.GroupSettings.Add(settings);

        await db.SaveChangesAsync();

        var sut   = MakeSut(db);
        var today = DateTime.UtcNow;

        // Act
        var result = await sut.InitiateMonthlyAsync(group.Id, today.Year, today.Month);

        // Assert
        result.Success.Should().BeTrue();

        var gkRecord = await db.MonthlyPayments.FirstAsync(
            m => m.PlayerId == gkPlyr.Id && m.Year == today.Year && m.Month == today.Month);

        gkRecord.Amount.Should().Be(100m);
    }

    // ── GetMyPendingItemsAsync — IsPaid flag ──────────────────────────────────

    [Fact]
    public async Task GetMyPendingItems_IncludesPaidMonthlyWithIsPaidTrue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetMyPendingItems_IncludesPaidMonthlyWithIsPaidTrue));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user   = new UserEntity("u", "F", "L", "u@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(75m);
        db.GroupSettings.Add(settings);

        var today = DateTime.UtcNow;
        var paidRecord = new MonthlyPaymentEntity(group.Id, player.Id, today.Year, today.Month, 75m);
        paidRecord.MarkAsPaid(null, null, null, null);
        db.MonthlyPayments.Add(paidRecord);

        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetMyPendingItemsAsync(group.Id, user.Id);

        // Assert
        result.Success.Should().BeTrue();
        var items = result.Data!;

        var paidItem = items.FirstOrDefault(i => i.IsPaid);
        paidItem.Should().NotBeNull("paid month must appear with IsPaid=true");
        paidItem!.Month.Should().Be(today.Month);
    }

    [Fact]
    public async Task GetMyPendingItems_IncludesPaidExtraChargeWithIsPaidTrue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetMyPendingItems_IncludesPaidExtraChargeWithIsPaidTrue));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user   = new UserEntity("u", "F", "L", "u@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);

        var charge  = new ExtraChargeEntity(group.Id, "Churrasco", null, 50m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);

        var payment = new ExtraChargePaymentEntity(charge.Id, player.Id, group.Id, 50m);
        payment.MarkAsPaid(null, null, null, null);
        db.ExtraChargePayments.Add(payment);

        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetMyPendingItemsAsync(group.Id, user.Id);

        // Assert
        result.Success.Should().BeTrue();
        var items = result.Data!;

        var paidExtra = items.FirstOrDefault(i => i.Type == BratnavaFC.Domain.Dtos.Payments.PendingPaymentType.Extra && i.IsPaid);
        paidExtra.Should().NotBeNull("paid extra charge must appear with IsPaid=true");
        paidExtra!.ChargeId.Should().Be(charge.Id);
    }

    // ── PaySelectedAsync — hook always fires ──────────────────────────────────

    [Fact]
    public async Task PaySelected_WhenAlreadyPaid_StillFiresCaixaHook()
    {
        // Regression: caixa hook must fire even when payment was already Paid.
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(PaySelected_WhenAlreadyPaid_StillFiresCaixaHook));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user   = new UserEntity("u", "F", "L", "u@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);

        var today = DateTime.UtcNow;
        var record = new MonthlyPaymentEntity(group.Id, player.Id, today.Year, today.Month, 75m);
        record.MarkAsPaid(null, null, null, null);   // already paid — no prior GT
        db.MonthlyPayments.Add(record);

        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        txMock
            .Setup(s => s.RecordOrRemovePaymentEntryAsync(
                It.IsAny<Guid>(), It.IsAny<TransactionSourceType>(), It.IsAny<Guid>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PaymentService(db, Mock.Of<IPushService>(), Mock.Of<ILogger<PaymentService>>(), txMock.Object);

        // Act — submit with IsPaid=true (already paid, but hook must still fire)
        var dto = new BratnavaFC.Domain.Dtos.Payments.PaySelectedDto
        {
            Items =
            [
                new BratnavaFC.Domain.Dtos.Payments.PaySelectedItem
                {
                    Type   = BratnavaFC.Domain.Dtos.Payments.PendingPaymentType.Monthly,
                    Year   = today.Year,
                    Month  = today.Month,
                    IsPaid = true,
                },
            ],
        };

        var result = await sut.PaySelectedAsync(group.Id, user.Id, dto);

        // Assert
        result.Success.Should().BeTrue();
        txMock.Verify(
            s => s.RecordOrRemovePaymentEntryAsync(
                group.Id, TransactionSourceType.MonthlyPayment, record.Id,
                75m, It.IsAny<string>(), It.IsAny<DateOnly>(),
                true, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "caixa hook must fire even when payment was already paid");
    }

    [Fact]
    public async Task PaySelected_WhenIsPaidFalse_MarksAsPendingAndFiresCaixaHookWithIsPaidFalse()
    {
        // Issue 3: uncheck = unpay — must mark as Pending and remove caixa entry.
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(PaySelected_WhenIsPaidFalse_MarksAsPendingAndFiresCaixaHookWithIsPaidFalse));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user   = new UserEntity("u", "F", "L", "u@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);

        var today  = DateTime.UtcNow;
        var record = new MonthlyPaymentEntity(group.Id, player.Id, today.Year, today.Month, 75m);
        record.MarkAsPaid(null, null, null, null);
        db.MonthlyPayments.Add(record);

        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        txMock
            .Setup(s => s.RecordOrRemovePaymentEntryAsync(
                It.IsAny<Guid>(), It.IsAny<TransactionSourceType>(), It.IsAny<Guid>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PaymentService(db, Mock.Of<IPushService>(), Mock.Of<ILogger<PaymentService>>(), txMock.Object);

        // Act — submit with IsPaid=false (uncheck = unpay)
        var dto = new BratnavaFC.Domain.Dtos.Payments.PaySelectedDto
        {
            Items =
            [
                new BratnavaFC.Domain.Dtos.Payments.PaySelectedItem
                {
                    Type   = BratnavaFC.Domain.Dtos.Payments.PendingPaymentType.Monthly,
                    Year   = today.Year,
                    Month  = today.Month,
                    IsPaid = false,
                },
            ],
        };

        var result = await sut.PaySelectedAsync(group.Id, user.Id, dto);

        // Assert
        result.Success.Should().BeTrue();

        // Payment must be Pending in DB
        var updated = await db.MonthlyPayments.FindAsync(record.Id);
        updated!.Status.Should().Be(PaymentStatus.Pending);

        // Caixa hook must fire with isPaid=false to remove the GT
        txMock.Verify(
            s => s.RecordOrRemovePaymentEntryAsync(
                group.Id, TransactionSourceType.MonthlyPayment, record.Id,
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                false, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "caixa hook must fire with isPaid=false to remove the GT");
    }

    [Fact]
    public async Task PaySelected_ExtraCharge_AlwaysFiresCaixaHook()
    {
        // Regression: extra charge caixa hook must fire regardless of prior paid state.
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(PaySelected_ExtraCharge_AlwaysFiresCaixaHook));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var user   = new UserEntity("u", "F", "L", "u@test.com", "h", null, null);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);

        var charge  = new ExtraChargeEntity(group.Id, "Hamburgada", null, 55m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);

        var payment = new ExtraChargePaymentEntity(charge.Id, player.Id, group.Id, 55m);
        // Payment starts as Pending
        db.ExtraChargePayments.Add(payment);

        await db.SaveChangesAsync();

        var txMock = new Mock<IFinancialTransactionService>();
        txMock
            .Setup(s => s.RecordOrRemovePaymentEntryAsync(
                It.IsAny<Guid>(), It.IsAny<TransactionSourceType>(), It.IsAny<Guid>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PaymentService(db, Mock.Of<IPushService>(), Mock.Of<ILogger<PaymentService>>(), txMock.Object);

        var dto = new BratnavaFC.Domain.Dtos.Payments.PaySelectedDto
        {
            Items =
            [
                new BratnavaFC.Domain.Dtos.Payments.PaySelectedItem
                {
                    Type     = BratnavaFC.Domain.Dtos.Payments.PendingPaymentType.Extra,
                    ChargeId = charge.Id,
                    IsPaid   = true,
                },
            ],
        };

        var result = await sut.PaySelectedAsync(group.Id, user.Id, dto);

        // Assert
        result.Success.Should().BeTrue();

        var updatedPayment = await db.ExtraChargePayments.FindAsync(payment.Id);
        updatedPayment!.Status.Should().Be(PaymentStatus.Paid);

        txMock.Verify(
            s => s.RecordOrRemovePaymentEntryAsync(
                group.Id, TransactionSourceType.ExtraCharge, payment.Id,
                55m, It.IsAny<string>(), It.IsAny<DateOnly>(),
                true, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "caixa hook must fire for extra charge payment");
    }
}
