using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Authentication;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class AuthenticationServiceTests
{
    private static IConfiguration CreateJwtConfig()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "THIS_IS_A_DEMO_SECRET_KEY_32CHARS_MIN____",
            ["Jwt:Issuer"] = "TeamManagement",
            ["Jwt:Audience"] = "account",
            ["Jwt:ExpiresSeconds"] = "300"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public async Task LoginAsync_WhenUserNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LoginAsync_WhenUserNotFound_ShouldReturnFailure));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new LoginDto(
            Username: "test@mail.com",
            Password: "123"
        );

        // Act
        var result = await sut.LoginAsync(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Usuário ou senha incorretos.");
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordInvalid_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LoginAsync_WhenPasswordInvalid_ShouldReturnFailure));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var user = new UserEntity("u", "f", "l", "mail@test.com", passwordHashed: "temp", phone: null, birthDate: null);
        var correctHash = hasher.HashPassword(user, "correct");
        user.SetPasswordHash(correctHash);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new LoginDto(
            Username: "u",
            Password: "wrong"
        );

        // Act
        var result = await sut.LoginAsync(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Usuário ou senha incorretos.");
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task LoginAsync_WhenValid_ShouldPersistRefreshToken_AndReturnJwtAndRefreshToken()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LoginAsync_WhenValid_ShouldPersistRefreshToken_AndReturnJwtAndRefreshToken));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var user = new UserEntity("u", "f", "l", "mail@test.com", passwordHashed: "temp", phone: null, birthDate: null, role: UserRole.Admin);
        user.SetPasswordHash(hasher.HashPassword(user, "pw"));

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new LoginDto(
            Username: "u",
            Password: "pw"
        );

        // Act
        var result = await sut.LoginAsync(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Token.Should().NotBeNullOrWhiteSpace();
        result.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var token = db.RefreshTokens.Local.First();

        var persisted = await db.RefreshTokens.FindAsync(token.Id);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(user.Id);
        persisted.Token.Should().Be(result.Data.RefreshToken);
        persisted.Expiration.Should().BeAfter(DateTime.UtcNow.AddDays(6)); // ~7 dias
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenTokenNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RefreshTokenAsync_WhenTokenNotFound_ShouldReturnFailure));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new RefreshTokenDto(
            RefreshToken: "nope"
        );

        // Act
        var result = await sut.RefreshTokenAsync(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("User is logged out, try again.");
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenExpired_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RefreshTokenAsync_WhenExpired_ShouldReturnFailure));

        var user = new UserEntity(
            userName: "user",
            firstName: "Luis",
            lastName: "Mello",
            email: "luis@mail.com",
            passwordHashed: "hash",
            phone: null,
            birthDate: null);

        db.Users.Add(user);

        var expiredToken = "expired_token_value";

        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Token = expiredToken,
            Expiration = DateTime.UtcNow.AddMinutes(-1), // expirado
            UserId = user.Id
        });

        await db.SaveChangesAsync();

        var logger = Mock.Of<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var sut = new AuthenticationService(db, logger, hasher, config);

        var request = new RefreshTokenDto(expiredToken);

        // Act
        var result = await sut.RefreshTokenAsync(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Refresh token expired.");
        result.Status.Should().Be(ResultStatus.BadRequest);
    }


    [Fact]
    public async Task RefreshTokenAsync_WhenValid_ShouldRotateToken_AndReturnNewJwt()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RefreshTokenAsync_WhenValid_ShouldRotateToken_AndReturnNewJwt));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var user = new UserEntity("u", "f", "l", "mail@test.com", passwordHashed: "x", phone: null, birthDate: null, role: UserRole.User);
        db.Users.Add(user);

        var token = new RefreshTokenEntity
        {
            Token = "old",
            Expiration = DateTime.UtcNow.AddDays(1),
            UserId = user.Id
        };

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        // Act
        var result = await sut.RefreshTokenAsync(new RefreshTokenDto(
            RefreshToken: "old"
        ), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Data.RefreshToken.Should().NotBe("old");

        var updated = await db.RefreshTokens.FindAsync(token.Id);
        updated!.Token.Should().Be(result.Data.RefreshToken);
        updated.Expiration.Should().BeAfter(DateTime.UtcNow.AddDays(6));
    }
}
