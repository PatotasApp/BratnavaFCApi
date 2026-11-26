using System.Text.Json;
using BratnavaFC.Application.Services;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public sealed class PushServicePayloadTests
{
    [Fact]
    public async Task SendToUserAsync_PersistsGroupAndUserInsideNavigationPayload()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var service = new PushService(db, Mock.Of<ILogger<PushService>>());

        await service.SendToUserAsync(
            userId,
            "Aviso",
            "Corpo",
            new Dictionary<string, string> { ["type"] = "test_notification" },
            groupId: groupId);

        var notification = await db.UserNotifications.SingleAsync();
        using var json = JsonDocument.Parse(notification.DataJson!);
        Assert.Equal(groupId.ToString(),
            json.RootElement.GetProperty("groupId").GetString());
        Assert.Equal(userId.ToString(),
            json.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task SendToUserAsync_UsesGenericTypeWhenCallerOmitsType()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var service = new PushService(db, Mock.Of<ILogger<PushService>>());

        await service.SendToUserAsync(Guid.NewGuid(), "Aviso", "Corpo");

        var notification = await db.UserNotifications.SingleAsync();
        Assert.Equal("generic", notification.Type);
    }
}
