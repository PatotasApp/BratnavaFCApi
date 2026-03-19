using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BranavaFC.Tests;

public class GroupEntityTests
{
    private static readonly Guid _creator = Guid.NewGuid();

    [Fact]
    public void Ctor_ShouldTrimName_AndSetSchedule_AndTouch()
    {
        // Arrange
        var schedule = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var group = new GroupEntity("  Bratnava FC  ", schedule, _creator);

        // Assert
        Assert.Equal("Bratnava FC", group.Name);
        Assert.Equal(schedule, group.ScheduleMatchDate);
        Assert.Equal(_creator, group.CreatedByUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithInvalidName_ShouldThrow(string? name)
    {
        // Arrange
        var group = new GroupEntity("ok", null, _creator);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.Rename(name!));
        Assert.Equal("Group name is required.", ex.Message);
    }

    [Fact]
    public void Rename_ShouldTrim_AndTouch()
    {
        // Arrange
        var group = new GroupEntity("Old", null, _creator);
        var before = DateTime.UtcNow;

        // Act
        group.Rename("  New Name  ");

        // Assert
        Assert.Equal("New Name", group.Name);
    }

    [Fact]
    public void Reschedule_ShouldSetValue_AndTouch()
    {
        // Arrange
        var group = new GroupEntity("G", null, _creator);
        var schedule = DateTimeOffset.UtcNow.AddDays(2);
        var before = DateTime.UtcNow;

        // Act
        group.Reschedule(schedule);

        // Assert
        Assert.Equal(schedule, group.ScheduleMatchDate);
    }

    [Fact]
    public void SetAdmins_Null_ShouldThrow()
    {
        // Arrange
        var group = new GroupEntity("G", null, _creator);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.SetAdmins(null!));
        Assert.Equal("Admins list is required.", ex.Message);
    }

    [Fact]
    public void SetAdmins_ShouldClear_AndAddDistinct_AndSetGroupId()
    {
        // Arrange
        var group = new GroupEntity("G", null, _creator);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Act
        group.SetAdmins(new[] { a, a, b });

        // Assert
        Assert.Equal(2, group.Admins.Count);

        foreach (var admin in group.Admins)
            Assert.Equal(group.Id, admin.GroupId);

        Assert.Contains(group.Admins, x => x.UserId == a);
        Assert.Contains(group.Admins, x => x.UserId == b);
    }

    [Fact]
    public void AddPlayer_Null_ShouldThrow()
    {
        // Arrange
        var group = new GroupEntity("G", null, _creator);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.AddPlayer(null!));
        Assert.Equal("Player is required.", ex.Message);
    }

    [Fact]
    public void AddPlayer_WhenUserAlreadyExists_ShouldThrow()
    {
        // Arrange
        var group = new GroupEntity("G", null, _creator);
        var userId = Guid.NewGuid();
        var player1 = new PlayerEntity("P1", userId, group.Id, 0, false, false, Status.Active);
        var player2 = new PlayerEntity("P2", userId, group.Id, 0, false, false, Status.Active);

        // Act
        group.AddPlayer(player1);
        var ex = Assert.Throws<InvalidOperationException>(() => group.AddPlayer(player2));

        // Assert
        Assert.Equal("Player already exists in the group.", ex.Message);
    }

    // ─── RemoveAdmin ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveAdmin_WhenUserIsCreator_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var creatorId = Guid.NewGuid();
        var group = new GroupEntity("G", null, creatorId);
        group.SetAdmins([creatorId]);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.RemoveAdmin(creatorId));
        Assert.Equal("The group creator cannot be removed from admins.", ex.Message);
    }

    [Fact]
    public void RemoveAdmin_WhenUserIsNotAdmin_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var creatorId = Guid.NewGuid();
        var group = new GroupEntity("G", null, creatorId);
        group.SetAdmins([creatorId]);

        var nonAdminId = Guid.NewGuid();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.RemoveAdmin(nonAdminId));
        Assert.Equal("User is not an admin of this group.", ex.Message);
    }

    [Fact]
    public void RemoveAdmin_WhenUserIsAdminAndNotCreator_ShouldRemoveSuccessfully()
    {
        // Arrange
        var creatorId = Guid.NewGuid();
        var adminId   = Guid.NewGuid();
        var group = new GroupEntity("G", null, creatorId);
        group.SetAdmins([creatorId, adminId]);

        Assert.Equal(2, group.Admins.Count);

        // Act
        group.RemoveAdmin(adminId);

        // Assert
        Assert.Single(group.Admins);
        Assert.DoesNotContain(group.Admins, a => a.UserId == adminId);
        Assert.Contains(group.Admins, a => a.UserId == creatorId);
    }

    // ─── TransferCreator ──────────────────────────────────────────────────────

    [Fact]
    public void TransferCreator_WithValidAdmin_UpdatesCreatedByUserId()
    {
        // Arrange
        var creatorId  = Guid.NewGuid();
        var newCreator = Guid.NewGuid();
        var group = new GroupEntity("G", null, creatorId);
        group.SetAdmins([creatorId, newCreator]);

        // Act
        group.TransferCreator(newCreator);

        // Assert
        Assert.Equal(newCreator, group.CreatedByUserId);
    }

    [Fact]
    public void TransferCreator_WithNonAdmin_ThrowsInvalidOperationException()
    {
        // Arrange
        var creatorId   = Guid.NewGuid();
        var nonAdminId  = Guid.NewGuid();
        var group = new GroupEntity("G", null, creatorId);
        group.SetAdmins([creatorId]);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.TransferCreator(nonAdminId));
        Assert.Equal("The new creator must be an existing admin of the group.", ex.Message);
    }

    [Fact]
    public void TransferCreator_AfterTransfer_RemoveAdmin_AllowsRemovingOldCreator()
    {
        // Arrange
        var oldCreatorId = Guid.NewGuid();
        var newCreatorId = Guid.NewGuid();
        var group = new GroupEntity("G", null, oldCreatorId);
        group.SetAdmins([oldCreatorId, newCreatorId]);

        // Act — transfere liderança; agora oldCreatorId não é mais o criador
        group.TransferCreator(newCreatorId);

        // O antigo criador deve poder ser removido dos admins porque CreatedByUserId mudou
        group.RemoveAdmin(oldCreatorId);

        // Assert
        Assert.DoesNotContain(group.Admins, a => a.UserId == oldCreatorId);
        Assert.Contains(group.Admins, a => a.UserId == newCreatorId);
    }
}
