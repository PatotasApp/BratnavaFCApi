using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests;

public class ClipCleanupJobTests
{
    private static ClipCleanupJob CreateSut(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        IReplayUrlService? r2 = null) =>
        new(db, r2 ?? Mock.Of<IReplayUrlService>(), NullLogger<ClipCleanupJob>.Instance);

    private static ReplayClipEntity MakeClip(
        string objectKey = "bucket/clip.mp4",
        DateTimeOffset? recordedAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "goal-replays", objectKey,
            "video/mp4", "etag", recordedAt ?? DateTimeOffset.UtcNow.AddDays(-30), MatchEventType.Gol);

    // ── 1. No clips ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenNoClips_ShouldNotCallR2()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenNoClips_ShouldNotCallR2));
        var r2 = new Mock<IReplayUrlService>();
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 2. Orphan clip (no likes, no favorites) ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenClipHasNoLikesOrFavorites_ShouldDeleteFromR2AndDb()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenClipHasNoLikesOrFavorites_ShouldDeleteFromR2AndDb));
        var clip = MakeClip("bucket/orphan.mp4");
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        r2.Setup(x => x.DeleteObjectAsync("bucket/orphan.mp4", It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync("bucket/orphan.mp4", It.IsAny<CancellationToken>()), Times.Once);
        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().BeEmpty();
    }

    // ── 3. Liked clip ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenClipHasLike_ShouldNotDelete()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenClipHasLike_ShouldNotDelete));
        var clip = MakeClip("bucket/liked.mp4");
        db.ReplayClips.Add(clip);
        db.ReplayLikes.Add(new ReplayLikeEntity(clip.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().HaveCount(1);
    }

    // ── 4. Favorited clip ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenClipHasFavorite_ShouldNotDelete()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenClipHasFavorite_ShouldNotDelete));
        var clip = MakeClip("bucket/favorited.mp4");
        db.ReplayClips.Add(clip);
        db.ReplayFavorites.Add(new ReplayFavoriteEntity(clip.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().HaveCount(1);
    }

    // ── 5. R2 throws → DB record must be preserved ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenR2DeleteThrows_ShouldPreserveDbRecord()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenR2DeleteThrows_ShouldPreserveDbRecord));
        var clip = MakeClip("bucket/failing.mp4");
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        r2.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new Exception("R2 unavailable"));
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — DB record must NOT be deleted if R2 removal failed
        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().HaveCount(1);
    }

    // ── 6. Mixed bag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithMixedClips_ShouldOnlyDeleteEligible()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WithMixedClips_ShouldOnlyDeleteEligible));

        var eligible    = MakeClip("bucket/eligible.mp4");
        var likedClip   = MakeClip("bucket/liked.mp4");
        var favClip     = MakeClip("bucket/favorited.mp4");

        db.ReplayClips.AddRange(eligible, likedClip, favClip);
        db.ReplayLikes.Add(new ReplayLikeEntity(likedClip.Id, Guid.NewGuid()));
        db.ReplayFavorites.Add(new ReplayFavoriteEntity(favClip.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        r2.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync("bucket/eligible.mp4",  It.IsAny<CancellationToken>()), Times.Once);
        r2.Verify(x => x.DeleteObjectAsync("bucket/liked.mp4",     It.IsAny<CancellationToken>()), Times.Never);
        r2.Verify(x => x.DeleteObjectAsync("bucket/favorited.mp4", It.IsAny<CancellationToken>()), Times.Never);

        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(c => c.ObjectKey == "bucket/eligible.mp4");
    }

    // ── 7. Recent clip (within 7-day grace period) ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveClip_WhenRecordedWithin7Days()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_ShouldPreserveClip_WhenRecordedWithin7Days));
        var recentClip = MakeClip("bucket/recent.mp4", DateTimeOffset.UtcNow.AddDays(-3));
        db.ReplayClips.Add(recentClip);
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().HaveCount(1);
    }

    // ── 8. Old clip (past 7-day grace period) ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteClip_WhenRecordedMoreThan7DaysAgo()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_ShouldDeleteClip_WhenRecordedMoreThan7DaysAgo));
        var oldClip = MakeClip("bucket/old.mp4", DateTimeOffset.UtcNow.AddDays(-8));
        db.ReplayClips.Add(oldClip);
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        r2.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync("bucket/old.mp4", It.IsAny<CancellationToken>()), Times.Once);
        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().BeEmpty();
    }

    // ── 9. Mixed ages — only old unengaged clips are deleted ─────────────────

    [Fact]
    public async Task ExecuteAsync_WithMixedAgeClips_ShouldOnlyDeleteOldUnengagedClips()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WithMixedAgeClips_ShouldOnlyDeleteOldUnengagedClips));

        var oldOrphan    = MakeClip("bucket/old-orphan.mp4",   DateTimeOffset.UtcNow.AddDays(-10));
        var recentOrphan = MakeClip("bucket/recent-orphan.mp4", DateTimeOffset.UtcNow.AddDays(-2));
        var oldLiked     = MakeClip("bucket/old-liked.mp4",    DateTimeOffset.UtcNow.AddDays(-15));

        db.ReplayClips.AddRange(oldOrphan, recentOrphan, oldLiked);
        db.ReplayLikes.Add(new ReplayLikeEntity(oldLiked.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var r2 = new Mock<IReplayUrlService>();
        r2.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
        var sut = CreateSut(db, r2.Object);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert
        r2.Verify(x => x.DeleteObjectAsync("bucket/old-orphan.mp4",    It.IsAny<CancellationToken>()), Times.Once);
        r2.Verify(x => x.DeleteObjectAsync("bucket/recent-orphan.mp4", It.IsAny<CancellationToken>()), Times.Never);
        r2.Verify(x => x.DeleteObjectAsync("bucket/old-liked.mp4",     It.IsAny<CancellationToken>()), Times.Never);

        var remaining = await db.ReplayClips.ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(c => c.ObjectKey == "bucket/old-orphan.mp4");
    }
}
