using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BranavaFC.Tests;

public class GroupEntityTests
{
    [Fact]
    public void Ctor_ShouldTrimName_AndSetSchedule_AndTouch()
    {
        // Arrange
        var schedule = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var group = new GroupEntity("  Bratnava FC  ", schedule);

        // Assert
        Assert.Equal("Bratnava FC", group.Name);
        Assert.Equal(schedule, group.ScheduleMatchDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithInvalidName_ShouldThrow(string? name)
    {
        // Arrange
        var group = new GroupEntity("ok", null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.Rename(name!));
        Assert.Equal("Group name is required.", ex.Message);
    }

    [Fact]
    public void Rename_ShouldTrim_AndTouch()
    {
        // Arrange
        var group = new GroupEntity("Old", null);
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
        var group = new GroupEntity("G", null);
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
        var group = new GroupEntity("G", null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.SetAdmins(null!));
        Assert.Equal("Admins list is required.", ex.Message);
    }

    [Fact]
    public void SetAdmins_ShouldClear_AndAddDistinct_AndSetGroupId()
    {
        // Arrange
        var group = new GroupEntity("G", null);
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
        var group = new GroupEntity("G", null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => group.AddPlayer(null!));
        Assert.Equal("Player is required.", ex.Message);
    }

    [Fact]
    public void AddPlayer_WhenUserAlreadyExists_ShouldThrow()
    {
        // Arrange
        var group = new GroupEntity("G", null);
        var userId = Guid.NewGuid();
        var player1 = new PlayerEntity("P1", userId, group.Id, 0, false, false, Status.Active);
        var player2 = new PlayerEntity("P2", userId, group.Id, 0, false, false, Status.Active);

        // Act
        group.AddPlayer(player1);
        var ex = Assert.Throws<InvalidOperationException>(() => group.AddPlayer(player2));

        // Assert
        Assert.Equal("Player already exists in the group.", ex.Message);
    }
}
