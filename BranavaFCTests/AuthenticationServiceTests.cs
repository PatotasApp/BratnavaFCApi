using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Authentication;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using static BratnavaFC.Domain.Dtos.Authentication.LoginContracts;

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
    public async Task LoginAsync_WhenUserNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LoginAsync_WhenUserNotFound_ShouldThrow));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new LoginContracts.Request(
            Email: "test@mail.com",
            Password: "123"
        );

        // Act
        var act = async () => await sut.LoginAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User not found");
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordInvalid_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LoginAsync_WhenPasswordInvalid_ShouldThrow));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var user = new UserEntity("u", "f", "l", "mail@test.com", passwordHashed: "temp", phone: null, birthDate: null);
        var correctHash = hasher.HashPassword(user, "correct");
        user.SetPasswordHash(correctHash);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new LoginContracts.Request(
            Email: "mail@test.com",
            Password: "wrong"
        );

        // Act
        var act = async () => await sut.LoginAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("Invalid user or password.");
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

        var request = new LoginContracts.Request(
            Email: "mail@test.com",
            Password: "pw"
        );

        // Act
        var res = await sut.LoginAsync(request, CancellationToken.None);

        // Assert
        res.Should().NotBeNull();
        var (jwt, refreshToken) = res;
        jwt.Should().NotBeNullOrWhiteSpace();
        refreshToken.Should().NotBeNullOrWhiteSpace();
        res.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var token = db.RefreshTokens.Local.First();

        var persisted = await db.RefreshTokens.FindAsync(token.Id);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(user.Id);
        persisted.Token.Should().Be(res.RefreshToken);
        persisted.Expiration.Should().BeAfter(DateTime.UtcNow.AddDays(6)); // ~7 dias
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenTokenNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RefreshTokenAsync_WhenTokenNotFound_ShouldThrow));

        var logger = new Mock<ILogger<AuthenticationService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var config = CreateJwtConfig();

        var sut = new AuthenticationService(db, logger.Object, hasher, config);

        var request = new LoginContracts.RefreshTokenRequest(
            RefreshToken: "nope"
        );

        // Act
        var act = async () => await sut.RefreshTokenAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User is logged out, try again.");
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenExpired_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RefreshTokenAsync_WhenExpired_ShouldThrow));

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

        var request = new LoginContracts.RefreshTokenRequest(expiredToken);

        // Act
        Func<Task> act = () => sut.RefreshTokenAsync(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<ApplicationException>()
            .WithMessage("Refresh token expired.");
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

        var request = new LoginContracts.RefreshTokenRequest(
            RefreshToken: "nope"
        );

        var res = await sut.RefreshTokenAsync(new LoginContracts.RefreshTokenRequest(
            RefreshToken: "old"
        ), CancellationToken.None);

        // Assert
        res.RefreshToken.Should().NotBeNullOrWhiteSpace();
        res.RefreshToken.Should().NotBe("old");

        var updated = await db.RefreshTokens.FindAsync(token.Id);
        updated!.Token.Should().Be(res.RefreshToken);
        updated.Expiration.Should().BeAfter(DateTime.UtcNow.AddDays(6));
    }
}
