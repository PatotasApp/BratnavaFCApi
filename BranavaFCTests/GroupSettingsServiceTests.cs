using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

public class GroupSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = new GroupSettingsService(db);

        // Act
        var result = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Group nao encontrado.");
    }

    [Fact]
    public async Task GetAsync_WhenNoSettings_ShouldReturnDefaults_IsPersistedFalse()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenNoSettings_ShouldReturnDefaults_IsPersistedFalse));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // Act
        var result = await sut.GetAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.GroupId.Should().Be(group.Id);
        result.Data.MinPlayers.Should().Be(5);
        result.Data.MaxPlayers.Should().Be(6);
        result.Data.IsPersisted.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertAsync_WhenNew_ShouldCreate_AndReturnPersistedTrue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenNew_ShouldCreate_AndReturnPersistedTrue));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers = 6,
            MaxPlayers = 12,
            DefaultPlaceName = " Boca ",
            DefaultDayOfWeek = DayOfWeek.Tuesday,
            DefaultKickoffTime = new TimeSpan(20, 30, 0)
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.IsPersisted.Should().BeTrue();
        result.Data.MinPlayers.Should().Be(6);
        result.Data.MaxPlayers.Should().Be(12);
        result.Data.DefaultPlaceName.Should().Be("Boca");

        var saved = await db.GroupSettings.FirstOrDefaultAsync(x => x.GroupId == group.Id);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertAsync_WhenExists_ShouldUpdate()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenExists_ShouldUpdate));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 6, null, null, null);
        db.GroupSettings.Add(settings);

        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers = 7,
            MaxPlayers = 10,
            DefaultPlaceName = "X",
            DefaultDayOfWeek = DayOfWeek.Friday,
            DefaultKickoffTime = new TimeSpan(21, 0, 0)
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MinPlayers.Should().Be(7);
        result.Data.MaxPlayers.Should().Be(10);
        result.Data.DefaultDayOfWeek.Should().Be(DayOfWeek.Friday);

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.MinPlayers.Should().Be(7);
        saved.MaxPlayers.Should().Be(10);
    }

    // ── ShowPlayerStats ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_WhenNoSettings_ShouldReturnShowPlayerStats_False()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenNoSettings_ShouldReturnShowPlayerStats_False));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // Act
        var result = await sut.GetAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ShowPlayerStats.Should().BeFalse();
        result.Data!.ShowStatsGeneralTab.Should().BeTrue();
        result.Data!.ShowStatsPerMatchTab.Should().BeTrue();
        result.Data!.ShowStatsClassificationTab.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_WhenAllStatsTabsDisabled_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenAllStatsTabsDisabled_ShouldFail));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers = 5,
            MaxPlayers = 10,
            ShowStatsGeneralTab = false,
            ShowStatsPerMatchTab = false,
            ShowStatsClassificationTab = false,
        };

        Func<Task> act = () => sut.UpsertAsync(group.Id, req, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*aba de estatisticas*");
    }

    [Fact]
    public async Task UpsertAsync_WhenNew_WithShowPlayerStatsTrue_ShouldPersistTrue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenNew_WithShowPlayerStatsTrue_ShouldPersistTrue));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers = 5,
            MaxPlayers = 10,
            ShowPlayerStats = true,
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ShowPlayerStats.Should().BeTrue();

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.ShowPlayerStats.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_WhenExists_WithShowPlayerStatsTrue_ShouldUpdateToTrue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenExists_WithShowPlayerStatsTrue_ShouldUpdateToTrue));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        // ShowPlayerStats começa false por padrão
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers = 5,
            MaxPlayers = 10,
            ShowPlayerStats = true,
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ShowPlayerStats.Should().BeTrue();

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.ShowPlayerStats.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_WhenShowPlayerStats_IsNull_ShouldNotChangeExistingValue()
    {
        // Arrange — cria settings com ShowPlayerStats = true
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenShowPlayerStats_IsNull_ShouldNotChangeExistingValue));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetShowPlayerStats(true);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // DTO sem ShowPlayerStats (null = não alterar)
        var req = new UpsertGroupSettingsDto
        {
            MinPlayers = 5,
            MaxPlayers = 10,
            ShowPlayerStats = null,
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert — valor anterior (true) deve ser mantido
        result.Success.Should().BeTrue();
        result.Data!.ShowPlayerStats.Should().BeTrue();

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.ShowPlayerStats.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_WhenSettingsPersisted_ShouldReturnShowPlayerStats()
    {
        // Arrange — salva settings com ShowPlayerStats = true e depois lê via GET
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenSettingsPersisted_ShouldReturnShowPlayerStats));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetShowPlayerStats(true);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // Act
        var result = await sut.GetAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ShowPlayerStats.Should().BeTrue();
        result.Data.IsPersisted.Should().BeTrue();
    }

    // ── GoalkeeperMonthlyFee ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_WhenNoSettings_ShouldReturnGoalkeeperMonthlyFee_Null()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenNoSettings_ShouldReturnGoalkeeperMonthlyFee_Null));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // Act
        var result = await sut.GetAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.GoalkeeperMonthlyFee.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_WhenNew_WithGoalkeeperFee_ShouldPersistBothFees()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenNew_WithGoalkeeperFee_ShouldPersistBothFees));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers           = 5,
            MaxPlayers           = 10,
            MonthlyFee           = 100m,
            GoalkeeperMonthlyFee = 60m,
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MonthlyFee.Should().Be(100m);
        result.Data.GoalkeeperMonthlyFee.Should().Be(60m);

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.MonthlyFee.Should().Be(100m);
        saved.GoalkeeperMonthlyFee.Should().Be(60m);
    }

    [Fact]
    public async Task UpsertAsync_WhenExists_WithGoalkeeperFee_ShouldUpdateFee()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenExists_WithGoalkeeperFee_ShouldUpdateFee));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers           = 5,
            MaxPlayers           = 10,
            MonthlyFee           = 100m,
            GoalkeeperMonthlyFee = 55m,
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.GoalkeeperMonthlyFee.Should().Be(55m);

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.GoalkeeperMonthlyFee.Should().Be(55m);
    }

    [Fact]
    public async Task UpsertAsync_WhenGoalkeeperFeeSetToNull_ShouldClearFee()
    {
        // Arrange — começa com ambas as fees e limpa a do goleiro
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenGoalkeeperFeeSetToNull_ShouldClearFee));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        settings.SetGoalkeeperMonthlyFee(60m);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        var req = new UpsertGroupSettingsDto
        {
            MinPlayers           = 5,
            MaxPlayers           = 10,
            MonthlyFee           = 100m,
            GoalkeeperMonthlyFee = null,
        };

        // Act
        var result = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.GoalkeeperMonthlyFee.Should().BeNull();

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.GoalkeeperMonthlyFee.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenPersistedWithBothFees_ShouldReturnBothFees()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenPersistedWithBothFees_ShouldReturnBothFees));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var settings = new GroupSettingsEntity(group.Id, 5, 10, null, null, null);
        settings.SetMonthlyFee(100m);
        settings.SetGoalkeeperMonthlyFee(60m);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // Act
        var result = await sut.GetAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MonthlyFee.Should().Be(100m);
        result.Data.GoalkeeperMonthlyFee.Should().Be(60m);
        result.Data.IsPersisted.Should().BeTrue();
    }
}
