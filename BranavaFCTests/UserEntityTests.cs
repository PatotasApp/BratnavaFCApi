using System;
using BratnavaFC.Domain.Entities;
using Xunit;

namespace BranavaFC.Tests;

public class UserEntityTests
{
    [Fact]
    public void Ctor_ShouldTrim_AndSetRole()
    {
        // Arrange
        var birth = DateTimeOffset.UtcNow.AddYears(-20);

        // Act
        var u = new UserEntity(
            "  luis  ",
            "  Luis  ",
            "  Mello  ",
            "  luis@email.com  ",
            "hash", // aqui sem espaços porque Password não dá trim no domínio
            "  11 99999-9999  ",
            birth,
            UserRole.Admin);

        // Assert
        Assert.Equal("luis", u.UserName);
        Assert.Equal("Luis", u.FirstName);
        Assert.Equal("Mello", u.LastName);
        Assert.Equal("luis@email.com", u.Email);
        Assert.Equal("hash", u.Password);
        Assert.Equal("11 99999-9999", u.Phone);
        Assert.Equal(birth, u.BirthDate);
        Assert.Equal(UserRole.Admin, u.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetUserName_WithInvalid_ShouldThrow(string userName)
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => u.SetUserName(userName));
        Assert.Equal("UserName is required.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetEmail_WithInvalid_ShouldThrow(string email)
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => u.SetEmail(email));
        Assert.Equal("Email is required.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetPasswordHash_WithInvalid_ShouldThrow(string passwordHash)
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => u.SetPasswordHash(passwordHash));
        Assert.Equal("Password hash is required.", ex.Message);
    }

    [Fact]
    public void UpdateProfile_WithInvalidFirstName_ShouldThrow()
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => u.UpdateProfile("   ", "X", null, null));
        Assert.Equal("FirstName is required.", ex.Message);
    }

    [Fact]
    public void UpdateProfile_WithInvalidLastName_ShouldThrow()
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => u.UpdateProfile("X", "   ", null, null));
        Assert.Equal("LastName is required.", ex.Message);
    }

    [Fact]
    public void UpdateProfile_ShouldTrim_AndNormalizePhoneToNull()
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", "  11999999999  ", null);

        // Act
        u.UpdateProfile("  Luis  ", "  Mello  ", null, "   ");

        // Assert
        Assert.Equal("Luis", u.FirstName);
        Assert.Equal("Mello", u.LastName);
        Assert.Null(u.Phone);
    }

    [Fact]
    public void SetRole_ShouldUpdateRole()
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act
        u.SetRole(UserRole.GodMode);

        // Assert
        Assert.Equal(UserRole.GodMode, u.Role);
    }

    [Fact]
    public void Inactivate_ShouldSetStatusToInactive_AndSetInactivatedAt()
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);

        // Act
        u.Inactivate();

        // Assert
        Assert.Equal(BratnavaFC.Domain.Enums.Status.Inactive, u.Status);
        Assert.NotNull(u.InactivatedAt);
    }

    [Fact]
    public void Reactivate_ShouldSetStatusToActive_AndClearInactivatedAt()
    {
        // Arrange
        var u = new UserEntity("u", "f", "l", "e", "p", null, null);
        u.Inactivate();

        // Act
        u.Reactivate();

        // Assert
        Assert.Equal(BratnavaFC.Domain.Enums.Status.Active, u.Status);
        Assert.Null(u.InactivatedAt);
    }
}