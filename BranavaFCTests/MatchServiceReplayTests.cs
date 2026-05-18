using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

public class MatchServiceReplayTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static MatchService CreateSut(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        IReplayUrlService? replayUrls = null)
    {
        var repo = new RepositoryBase<MatchEntity>(db);
        var push = Mock.Of<IPushService>();
        var urls = replayUrls ?? Mock.Of<IReplayUrlService>();
        return new MatchService(db, repo, push, urls, Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>());
    }

    private static ReplayClipEntity MakeClip(
        Guid groupId, Guid matchId,
        string objectKey,
        MatchEventType eventType,
        DateTimeOffset? uploadedAt = null) =>
        new(groupId, matchId, "goal-replays", objectKey, "video/mp4", "etag",
            uploadedAt ?? DateTimeOffset.UtcNow, eventType);

    // ─── GetReplaysAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetReplaysAsync_WhenNoClipsExist_ShouldReturnEmptyList()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetReplaysAsync_WhenNoClipsExist_ShouldReturnEmptyList));
        var sut = CreateSut(db);

        // Act
        var result = await sut.GetReplaysAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReplaysAsync_WhenClipsExist_ShouldReturnDtosWithPresignedUrls()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetReplaysAsync_WhenClipsExist_ShouldReturnDtosWithPresignedUrls));

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        db.ReplayClips.Add(MakeClip(groupId, matchId, "gols/193010_gol_FULL.mp4", MatchEventType.Gol));
        await db.SaveChangesAsync();

        var urlService = new Mock<IReplayUrlService>();
        urlService
            .Setup(s => s.GeneratePresignedUrl("gols/193010_gol_FULL.mp4"))
            .Returns("https://r2.example.com/presigned?token=abc");

        var sut = CreateSut(db, urlService.Object);

        // Act
        var result = await sut.GetReplaysAsync(groupId, matchId, null, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);

        var dto = result.Data![0];
        dto.ObjectKey.Should().Be("gols/193010_gol_FULL.mp4");
        dto.VideoUrl.Should().Be("https://r2.example.com/presigned?token=abc");
        dto.EventType.Should().Be("Gol");
    }

    [Fact]
    public async Task GetReplaysAsync_ShouldFilterByGroupIdAndMatchId()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetReplaysAsync_ShouldFilterByGroupIdAndMatchId));

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        db.ReplayClips.AddRange(
            MakeClip(groupId, matchId,         "correct.mp4",     MatchEventType.Gol),
            MakeClip(groupId, Guid.NewGuid(),  "wrong-match.mp4", MatchEventType.Gol),
            MakeClip(Guid.NewGuid(), matchId,  "wrong-group.mp4", MatchEventType.Gol)
        );
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetReplaysAsync(groupId, matchId, null, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].ObjectKey.Should().Be("correct.mp4");
    }

    [Fact]
    public async Task GetReplaysAsync_ShouldReturnClipsOrderedByUploadedAt()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetReplaysAsync_ShouldReturnClipsOrderedByUploadedAt));

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;

        db.ReplayClips.AddRange(
            MakeClip(groupId, matchId, "third.mp4",  MatchEventType.Jogada, baseTime.AddMinutes(2)),
            MakeClip(groupId, matchId, "first.mp4",  MatchEventType.Gol,    baseTime),
            MakeClip(groupId, matchId, "second.mp4", MatchEventType.Gol,    baseTime.AddMinutes(1))
        );
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetReplaysAsync(groupId, matchId, null, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Select(d => d.ObjectKey)
            .Should().ContainInOrder("first.mp4", "second.mp4", "third.mp4");
    }

    [Fact]
    public async Task GetReplaysAsync_ShouldMapGolAndJogadaEventTypesToString()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetReplaysAsync_ShouldMapGolAndJogadaEventTypesToString));

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;

        db.ReplayClips.AddRange(
            MakeClip(groupId, matchId, "gol.mp4",    MatchEventType.Gol,    baseTime),
            MakeClip(groupId, matchId, "jogada.mp4", MatchEventType.Jogada, baseTime.AddSeconds(1))
        );
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetReplaysAsync(groupId, matchId, null, CancellationToken.None);

        // Assert
        result.Data![0].EventType.Should().Be("Gol");
        result.Data[1].EventType.Should().Be("Jogada");
    }

    [Fact]
    public async Task GetReplaysAsync_ShouldCallPresignedUrlForEachClip()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetReplaysAsync_ShouldCallPresignedUrlForEachClip));

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        db.ReplayClips.AddRange(
            MakeClip(groupId, matchId, "clip1.mp4", MatchEventType.Gol),
            MakeClip(groupId, matchId, "clip2.mp4", MatchEventType.Jogada)
        );
        await db.SaveChangesAsync();

        var urlService = new Mock<IReplayUrlService>();
        urlService.Setup(s => s.GeneratePresignedUrl(It.IsAny<string>())).Returns("https://url");

        var sut = CreateSut(db, urlService.Object);

        // Act
        await sut.GetReplaysAsync(groupId, matchId, null, CancellationToken.None);

        // Assert
        urlService.Verify(s => s.GeneratePresignedUrl("clip1.mp4"), Times.Once);
        urlService.Verify(s => s.GeneratePresignedUrl("clip2.mp4"), Times.Once);
    }
}
