using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public class GroupSettingsEntityTests
{
    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        // Arrange + Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new GroupSettingsEntity(Guid.Empty, 6, 12, null, null, null));

        Assert.Equal("GroupId é obrigatório.", ex.Message);
    }

    [Theory]
    [InlineData(0, 10, "MinPlayers deve ser maior que 0.")]
    [InlineData(-1, 10, "MinPlayers deve ser maior que 0.")]
    [InlineData(1, 0, "MaxPlayers deve ser maior que 0.")]
    [InlineData(10, 9, "MinPlayers não pode ser maior que MaxPlayers.")]
    [InlineData(1, 23, "MaxPlayers não pode ser maior que 22.")]
    public void Ctor_WithInvalidLimits_ShouldThrow(int min, int max, string msg)
    {
        // Arrange
        var groupId = Guid.NewGuid();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new GroupSettingsEntity(groupId, min, max, null, null, null));

        Assert.Equal(msg, ex.Message);
    }

    [Fact]
    public void Ctor_ShouldTrimPlaceName_AndSetSchedule()
    {
        // Arrange
        var groupId = Guid.NewGuid();

        // Act
        var s = new GroupSettingsEntity(groupId, 6, 12, "  Boca Jrs  ", DayOfWeek.Tuesday, new TimeSpan(20, 30, 0));

        // Assert
        Assert.Equal(groupId, s.GroupId);
        Assert.Equal(6, s.MinPlayers);
        Assert.Equal(12, s.MaxPlayers);
        Assert.Equal("Boca Jrs", s.DefaultPlaceName);
        Assert.Equal(DayOfWeek.Tuesday, s.DefaultDayOfWeek);
        Assert.Equal(new TimeSpan(20, 30, 0), s.DefaultKickoffTime);
    }

    [Fact]
    public void SetDefaultPlaceName_WhenWhitespace_ShouldBecomeNull()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var s = new GroupSettingsEntity(groupId, 6, 12, "X", null, null);

        // Act
        s.Update(6, 12, "   ", null, null);

        // Assert
        Assert.Null(s.DefaultPlaceName);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(24, 0, 0)]
    public void Update_WithInvalidKickoff_ShouldThrow(int hours, int minutes, int seconds)
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var s = new GroupSettingsEntity(groupId, 6, 12, null, null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            s.Update(6, 12, null, DayOfWeek.Monday, new TimeSpan(hours, minutes, seconds)));

        Assert.Equal("DefaultKickoffTime inválido.", ex.Message);
    }
}
