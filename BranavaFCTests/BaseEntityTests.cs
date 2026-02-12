namespace BranavaFC.Tests;

public class BaseEntityTests
{
    [Fact]
    public void Ctor_ShouldInitialize_Id_And_CreateDate()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var entity = new TestBaseEntity();

        // Assert
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.True(entity.CreateDate >= before);
        Assert.Null(entity.UpdateDate);
    }

    [Fact]
    public void Touch_ShouldSet_UpdateDate()
    {
        // Arrange
        var entity = new TestBaseEntity();
        var before = DateTime.UtcNow;

        // Act
        entity.Touch();

        // Assert
        Assert.NotNull(entity.UpdateDate);
        Assert.True(entity.UpdateDate >= before);
    }
}
