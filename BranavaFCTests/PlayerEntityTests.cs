using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BranavaFC.Tests;

public class PlayerEntityTests
{
    [Fact]
    public void Ctor_ShouldSetFields_AndTrimName()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        // Act
        var p = new PlayerEntity("  Caio  ", userId, groupId, 12.5m, true, Status.Active);

        // Assert
        Assert.Equal("Caio", p.Name);
        Assert.Equal(userId, p.UserId);
        Assert.Equal(groupId, p.GroupId);
        Assert.Equal(12.5m, p.SkillPoints);
        Assert.True(p.IsGoalkeeper);
        Assert.NotNull(p.UpdateDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithInvalidName_ShouldThrow(string? name)
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0, false, Status.Active);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => p.Rename(name!));
        Assert.Equal("Player name is required.", ex.Message);
    }

    [Fact]
    public void SetUser_WithEmpty_ShouldThrow()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0, false, Status.Active);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => p.SetUser(Guid.Empty));
        Assert.Equal("UserId is required.", ex.Message);
    }

    [Fact]
    public void SetGroup_WithEmpty_ShouldThrow()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0, false, Status.Active);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => p.SetGroup(Guid.Empty));
        Assert.Equal("GroupId is required.", ex.Message);
    }

    [Fact]
    public void SetSkillPoints_WithNegative_ShouldThrow()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0, false, Status.Active);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => p.SetSkillPoints(-0.1m));
        Assert.Equal("SkillPoints cannot be negative.", ex.Message);
    }
}
