namespace BranavaFC.Tests;

using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Time;
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
    public void EnsureUtc_WhenDateTimeIsUnspecified_ShouldMarkAsUtcWithoutChangingClock()
    {
        var unspecified = DateTime.SpecifyKind(new DateTime(2026, 7, 18, 10, 0, 0), DateTimeKind.Unspecified);

        var result = BratnavaDateTime.EnsureUtc(unspecified);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc), result);
    }
}
