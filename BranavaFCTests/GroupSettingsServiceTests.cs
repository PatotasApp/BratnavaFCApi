using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

public class GroupSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_WhenGroupNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenGroupNotFound_ShouldThrow));
        var sut = new GroupSettingsService(db);

        // Act
        var act = async () => await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Group nao encontrado.");
    }

    [Fact]
    public async Task GetAsync_WhenNoSettings_ShouldReturnDefaults_IsPersistedFalse()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAsync_WhenNoSettings_ShouldReturnDefaults_IsPersistedFalse));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new GroupSettingsService(db);

        // Act
        var dto = await sut.GetAsync(group.Id, CancellationToken.None);

        // Assert
        dto.GroupId.Should().Be(group.Id);
        dto.MinPlayers.Should().Be(5);
        dto.MaxPlayers.Should().Be(6);
        dto.IsPersisted.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertAsync_WhenNew_ShouldCreate_AndReturnPersistedTrue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenNew_ShouldCreate_AndReturnPersistedTrue));

        var group = new GroupEntity("G", null);
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
        var dto = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        dto.IsPersisted.Should().BeTrue();
        dto.MinPlayers.Should().Be(6);
        dto.MaxPlayers.Should().Be(12);
        dto.DefaultPlaceName.Should().Be("Boca");

        var saved = await db.GroupSettings.FirstOrDefaultAsync(x => x.GroupId == group.Id);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertAsync_WhenExists_ShouldUpdate()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpsertAsync_WhenExists_ShouldUpdate));

        var group = new GroupEntity("G", null);
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
        var dto = await sut.UpsertAsync(group.Id, req, CancellationToken.None);

        // Assert
        dto.MinPlayers.Should().Be(7);
        dto.MaxPlayers.Should().Be(10);
        dto.DefaultDayOfWeek.Should().Be(DayOfWeek.Friday);

        var saved = await db.GroupSettings.FirstAsync(x => x.GroupId == group.Id);
        saved.MinPlayers.Should().Be(7);
        saved.MaxPlayers.Should().Be(10);
    }
}
