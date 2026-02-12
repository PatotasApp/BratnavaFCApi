using BratnavaFC.Domain.Enums;

namespace BranavaFC.Tests;

public class InactivatableEntityTests
{
    [Fact]
    public void Inactivate_WhenActive_ShouldSetInactive_AndDate()
    {
        // Arrange
        var entity = new TestInactivatableEntity();
        var before = DateTime.UtcNow;

        // Act
        entity.Inactivate();

        // Assert
        Assert.Equal(Status.Inactive, entity.Status);
        Assert.NotNull(entity.InactivatedAt);
        Assert.True(entity.InactivatedAt >= before);
        Assert.True(entity.IsInactive());
    }

    [Fact]
    public void Inactivate_WhenAlreadyInactive_ShouldBeIdempotent()
    {
        // Arrange
        var entity = new TestInactivatableEntity();
        entity.Inactivate();
        var first = entity.InactivatedAt;

        // Act
        entity.Inactivate();

        // Assert
        Assert.Equal(Status.Inactive, entity.Status);
        Assert.Equal(first, entity.InactivatedAt);
    }

    [Fact]
    public void Reactivate_WhenInactive_ShouldSetActive_AndClearDate()
    {
        // Arrange
        var entity = new TestInactivatableEntity();
        entity.Inactivate();

        // Act
        entity.Reactivate();

        // Assert
        Assert.Equal(Status.Active, entity.Status);
        Assert.Null(entity.InactivatedAt);
        Assert.False(entity.IsInactive());
    }

    [Fact]
    public void Reactivate_WhenAlreadyActive_ShouldBeIdempotent()
    {
        // Arrange
        var entity = new TestInactivatableEntity();

        // Act
        entity.Reactivate();

        // Assert
        Assert.Equal(Status.Active, entity.Status);
        Assert.Null(entity.InactivatedAt);
    }
}
