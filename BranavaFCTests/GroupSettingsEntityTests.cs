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

        Assert.Equal("GroupId e obrigatorio.", ex.Message);
    }

    [Theory]
    [InlineData(0, 10, "MinPlayers deve ser maior que 0.")]
    [InlineData(-1, 10, "MinPlayers deve ser maior que 0.")]
    [InlineData(1, 0, "MaxPlayers deve ser maior que 0.")]
    [InlineData(10, 9, "MinPlayers nao pode ser maior que MaxPlayers.")]
    [InlineData(1, 23, "MaxPlayers nao pode ser maior que 22.")]
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

        Assert.Equal("DefaultKickoffTime invalido.", ex.Message);
    }

    // ── ShowPlayerStats ───────────────────────────────────────────────────────

    [Fact]
    public void ShowPlayerStats_DefaultShouldBeFalse()
    {
        // Arrange + Act
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);

        // Assert
        Assert.False(s.ShowPlayerStats);
    }

    [Fact]
    public void SetShowPlayerStats_ShouldSetToTrue()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);

        // Act
        s.SetShowPlayerStats(true);

        // Assert
        Assert.True(s.ShowPlayerStats);
    }

    [Fact]
    public void SetShowPlayerStats_ShouldSetBackToFalse()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);
        s.SetShowPlayerStats(true);

        // Act
        s.SetShowPlayerStats(false);

        // Assert
        Assert.False(s.ShowPlayerStats);
    }

    // ── GoalkeeperMonthlyFee ──────────────────────────────────────────────────

    [Fact]
    public void GoalkeeperMonthlyFee_Default_ShouldBeNull()
    {
        // Arrange + Act
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);

        // Assert
        Assert.Null(s.GoalkeeperMonthlyFee);
    }

    [Fact]
    public void SetGoalkeeperMonthlyFee_WithPositiveValue_ShouldSet()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);

        // Act
        s.SetGoalkeeperMonthlyFee(60m);

        // Assert
        Assert.Equal(60m, s.GoalkeeperMonthlyFee);
    }

    [Fact]
    public void SetGoalkeeperMonthlyFee_WithZero_ShouldAccept()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);

        // Act
        s.SetGoalkeeperMonthlyFee(0m);

        // Assert
        Assert.Equal(0m, s.GoalkeeperMonthlyFee);
    }

    [Fact]
    public void SetGoalkeeperMonthlyFee_WithNull_ShouldClear()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);
        s.SetGoalkeeperMonthlyFee(60m);

        // Act
        s.SetGoalkeeperMonthlyFee(null);

        // Assert
        Assert.Null(s.GoalkeeperMonthlyFee);
    }

    [Fact]
    public void SetGoalkeeperMonthlyFee_WithNegativeValue_ShouldThrow()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => s.SetGoalkeeperMonthlyFee(-1m));
        Assert.Equal("GoalkeeperMonthlyFee nao pode ser negativo.", ex.Message);
    }

    [Fact]
    public void SetGoalkeeperMonthlyFee_IsIndependentOfMonthlyFee()
    {
        // Arrange
        var s = new GroupSettingsEntity(Guid.NewGuid(), 5, 10, null, null, null);
        s.SetMonthlyFee(100m);

        // Act
        s.SetGoalkeeperMonthlyFee(60m);

        // Assert — both fees coexist independently
        Assert.Equal(100m, s.MonthlyFee);
        Assert.Equal(60m, s.GoalkeeperMonthlyFee);
    }
}
