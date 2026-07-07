using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class MiscEntityTests
{
    // ─── PushTokenEntity ──────────────────────────────────────────────────────

    [Fact]
    public void PushToken_Ctor_Valid_TrimsToken_LowersPlatform_AndActivates()
    {
        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var token = new PushTokenEntity(userId, "  abc123  ", "  ANDROID  ");

        token.UserId.Should().Be(userId);
        token.Token.Should().Be("abc123");
        token.Platform.Should().Be("android");
        token.IsActive.Should().BeTrue();
        token.UpdatedAt.Should().BeOnOrAfter(before);
        token.User.Should().BeNull();
    }

    [Fact]
    public void PushToken_Ctor_WithEmptyUserId_Throws()
    {
        var act = () => new PushTokenEntity(Guid.Empty, "abc", "ios");
        act.Should().Throw<InvalidOperationException>().WithMessage("UserId é obrigatório.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PushToken_Ctor_WithInvalidToken_Throws(string? token)
    {
        var act = () => new PushTokenEntity(Guid.NewGuid(), token!, "ios");
        act.Should().Throw<InvalidOperationException>().WithMessage("Token é obrigatório.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PushToken_Ctor_WithInvalidPlatform_Throws(string? platform)
    {
        var act = () => new PushTokenEntity(Guid.NewGuid(), "abc", platform!);
        act.Should().Throw<InvalidOperationException>().WithMessage("Platform é obrigatória.");
    }

    [Fact]
    public void PushToken_Deactivate_SetsInactive_AndBumpsUpdatedAt()
    {
        var token = new PushTokenEntity(Guid.NewGuid(), "abc", "ios");
        var previous = token.UpdatedAt;

        token.Deactivate();

        token.IsActive.Should().BeFalse();
        token.UpdatedAt.Should().BeOnOrAfter(previous);
    }

    [Fact]
    public void PushToken_Reactivate_SetsActive_AndBumpsUpdatedAt()
    {
        var token = new PushTokenEntity(Guid.NewGuid(), "abc", "ios");
        token.Deactivate();
        var previous = token.UpdatedAt;

        token.Reactivate();

        token.IsActive.Should().BeTrue();
        token.UpdatedAt.Should().BeOnOrAfter(previous);
    }

    [Fact]
    public void PushToken_Touch_OnlyBumpsUpdatedAt()
    {
        var token = new PushTokenEntity(Guid.NewGuid(), "abc", "android");
        var previous = token.UpdatedAt;

        token.Touch();

        token.IsActive.Should().BeTrue("Touch must not change activation state");
        token.Token.Should().Be("abc");
        token.UpdatedAt.Should().BeOnOrAfter(previous);
    }

    // ─── ScheduledNotificationJobEntity ──────────────────────────────────────

    [Fact]
    public void ScheduledNotificationJob_Ctor_SetsAllProperties()
    {
        var entityId = Guid.NewGuid();
        var scheduledFor = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        var job = new ScheduledNotificationJobEntity("match", entityId, "24h", "hf-42", scheduledFor);

        job.EntityType.Should().Be("match");
        job.EntityId.Should().Be(entityId);
        job.TriggerType.Should().Be("24h");
        job.HangfireJobId.Should().Be("hf-42");
        job.ScheduledForUtc.Should().Be(scheduledFor);
    }
}
