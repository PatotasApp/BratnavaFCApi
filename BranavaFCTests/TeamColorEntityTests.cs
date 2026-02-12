// Tests/Domain/Entities/TeamColorEntityTests.cs
using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public class TeamColorEntityTests
{
    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        // Arrange + Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new TeamColorEntity(Guid.Empty, "Azul", "#0011AA"));

        Assert.Equal("GroupId e obrigatorio.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetName_WithInvalid_ShouldThrow(string name)
    {
        // Arrange
        var c = new TeamColorEntity(Guid.NewGuid(), "Ok", "#0011AA");

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => c.SetName(name));
        Assert.Equal("Nome da cor e obrigatorio.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetHexValue_WhenEmptyOrWhitespace_ShouldThrowRequired(string hex)
    {
        // Arrange
        var c = new TeamColorEntity(Guid.NewGuid(), "Ok", "#0011AA");

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => c.SetHexValue(hex));
        Assert.Equal("Hex da cor e obrigatorio.", ex.Message);
    }

    [Theory]
    [InlineData("GGGGGG")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#ZZZZZZ")]
    public void SetHexValue_WhenFormatInvalid_ShouldThrowInvalidHex(string hex)
    {
        // Arrange
        var c = new TeamColorEntity(Guid.NewGuid(), "Ok", "#0011AA");

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => c.SetHexValue(hex));
        Assert.Equal("Hex invalido. Use o formato #RRGGBB (ex: #1A2B3C).", ex.Message);
    }

    [Fact]
    public void SetHexValue_ShouldNormalize_ToUpper_AndEnsureHash()
    {
        // Arrange
        var c = new TeamColorEntity(Guid.NewGuid(), "Ok", "#0011AA");

        // Act
        c.SetHexValue("1a2b3c");

        // Assert
        Assert.Equal("#1A2B3C", c.HexValue);
    }

    [Fact]
    public void Activate_Inactivate_ShouldToggle()
    {
        // Arrange
        var c = new TeamColorEntity(Guid.NewGuid(), "Ok", "#0011AA");

        // Act
        c.Inactivate();

        // Assert
        Assert.False(c.IsActive);

        // Act
        c.Activate();

        // Assert
        Assert.True(c.IsActive);
    }
}
