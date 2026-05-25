using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace BratnavaFC.Tests;

/// <summary>
/// Testa o método RevokeTokenAsync do AuthenticationService.
///
/// Cenários cobertos:
///   1. Token existente é removido do banco.
///   2. Token inexistente não lança exceção.
///   3. Revogar token de um usuário não afeta tokens de outros usuários.
/// </summary>
public class AuthenticationServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AuthenticationService CreateService(AppDbContext db)
    {
        var logger        = Substitute.For<ILogger<AuthenticationService>>();
        var passwordHasher = new PasswordHasher<UserEntity>();
        var configuration  = Substitute.For<IConfiguration>();
        return new AuthenticationService(db, logger, passwordHasher, configuration);
    }

    private static UserEntity MakeUser(string suffix) =>
        new($"user{suffix}", "First", "Last", $"user{suffix}@test.com", "hash", null, null);

    private static RefreshTokenEntity MakeToken(string tokenValue, UserEntity user) =>
        new()
        {
            Token      = tokenValue,
            UserId     = user.Id,
            Expiration = DateTimeOffset.UtcNow.AddDays(7),
            User       = user,
        };

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeTokenAsync_ExistingToken_RemovesItFromDatabase()
    {
        // Arrange
        await using var db = CreateDb();

        var user  = MakeUser("a");
        var token = MakeToken("valid-token-abc", user);

        db.Users.Add(user);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Act
        await service.RevokeTokenAsync("valid-token-abc", CancellationToken.None);

        // Assert
        var remaining = await db.RefreshTokens.CountAsync();
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task RevokeTokenAsync_NonExistentToken_DoesNotThrow()
    {
        // Arrange
        await using var db = CreateDb();
        var service = CreateService(db);

        // Act & Assert — must not throw even when the token is absent
        var exception = await Record.ExceptionAsync(() =>
            service.RevokeTokenAsync("ghost-token-xyz", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RevokeTokenAsync_DoesNotRemoveOtherUsersTokens()
    {
        // Arrange
        await using var db = CreateDb();

        var user1 = MakeUser("1");
        var user2 = MakeUser("2");

        var token1 = MakeToken("token-user1", user1);
        var token2 = MakeToken("token-user2", user2);

        db.Users.AddRange(user1, user2);
        db.RefreshTokens.AddRange(token1, token2);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Act — revoke only user1's token
        await service.RevokeTokenAsync("token-user1", CancellationToken.None);

        // Assert — user2's token is untouched
        var remaining = await db.RefreshTokens.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("token-user2", remaining[0].Token);
    }

    [Fact]
    public async Task RevokeTokenAsync_AfterRevoke_SameTokenCannotBeFound()
    {
        // Arrange
        await using var db = CreateDb();

        var user  = MakeUser("b");
        var token = MakeToken("one-time-token", user);

        db.Users.Add(user);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Act
        await service.RevokeTokenAsync("one-time-token", CancellationToken.None);

        // Assert — attempting to revoke again silently succeeds (no double-delete error)
        var exception = await Record.ExceptionAsync(() =>
            service.RevokeTokenAsync("one-time-token", CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(0, await db.RefreshTokens.CountAsync());
    }
}
