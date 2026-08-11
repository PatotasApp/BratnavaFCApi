using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace BratnavaFC.Tests;

public class PushTokenLifecycleTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static PushService CreateService(AppDbContext db) =>
        new(db, Substitute.For<ILogger<PushService>>());

    [Fact]
    public async Task UnregisterTokenAsync_DeactivatesOnlyCurrentUserToken()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        db.PushTokens.AddRange(
            new PushTokenEntity(userId, "device-token", "android"),
            new PushTokenEntity(otherUserId, "device-token", "android"));
        await db.SaveChangesAsync();

        await CreateService(db).UnregisterTokenAsync(
            userId, "device-token", CancellationToken.None);

        var rows = await db.PushTokens.ToListAsync();
        Assert.False(rows.Single(x => x.UserId == userId).IsActive);
        Assert.True(rows.Single(x => x.UserId == otherUserId).IsActive);
    }

    [Fact]
    public async Task RegisterTokenAsync_DeactivatesPreviousOwnerOnSameDevice()
    {
        await using var db = CreateDb();
        var previousUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        db.PushTokens.Add(new PushTokenEntity(
            previousUserId, "device-token", "android"));
        await db.SaveChangesAsync();

        await CreateService(db).RegisterTokenAsync(
            currentUserId, "device-token", "android", CancellationToken.None);

        var rows = await db.PushTokens.ToListAsync();
        Assert.False(rows.Single(x => x.UserId == previousUserId).IsActive);
        Assert.True(rows.Single(x => x.UserId == currentUserId).IsActive);
    }
}
