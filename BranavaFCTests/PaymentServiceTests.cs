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
        new(db, Mock.Of<IPushService>(), Mock.Of<ILogger<PaymentService>>());

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
}
