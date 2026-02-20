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
}
