using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

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
        p.Name.Should().Be("Caio");
        p.UserId.Should().Be(userId);
        p.GroupId.Should().Be(groupId);
        p.SkillPoints.Should().Be(12.5m);
        p.IsGoalkeeper.Should().BeTrue();
        p.Status.Should().Be(Status.Active);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_WithInvalidName_ShouldThrow(string? name)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        // Act
        var act = () => new PlayerEntity(name!, userId, groupId, 0m, false, Status.Active);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Player name is required.");
    }

    [Fact]
    public void Rename_ShouldTrimName()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, Status.Active);

        // Act
        p.Rename("  Marlon  ");

        // Assert
        p.Name.Should().Be("Marlon");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithInvalidName_ShouldThrow(string? name)
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, Status.Active);

        // Act
        var act = () => p.Rename(name!);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Player name is required.");
    }

    [Fact]
    public void SetUser_WithEmpty_ShouldThrow()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, Status.Active);

        // Act
        var act = () => p.SetUser(Guid.Empty);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("UserId is required.");
    }

    [Fact]
    public void SetGroup_WithEmpty_ShouldThrow()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, Status.Active);

        // Act
        var act = () => p.SetGroup(Guid.Empty);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("GroupId is required.");
    }

    [Fact]
    public void SetSkillPoints_WithNegative_ShouldThrow()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, Status.Active);

        // Act
        var act = () => p.SetSkillPoints(-0.1m);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("SkillPoints cannot be negative.");
    }

    [Fact]
    public void SetSkillPoints_WithValid_ShouldUpdate()
    {
        // Arrange
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, Status.Active);

        // Act
        p.SetSkillPoints(7.25m);

        // Assert
        p.SkillPoints.Should().Be(7.25m);
    }
}