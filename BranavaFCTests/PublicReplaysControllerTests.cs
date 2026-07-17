using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BranavaFC.Tests;

public sealed class PublicReplaysControllerTests
{
    private static ReplayClipEntity MakeClip(
        Guid groupId,
        Guid matchId,
        string objectKey,
        MatchEventType eventType,
        DateTimeOffset recordedAt) =>
        new(groupId, matchId, "goal-replays", objectKey, "video/mp4", "etag", recordedAt, eventType);

    [Fact]
    public async Task GetMatchReplays_ShouldReturnAllGoalTypesForExternalShare()
    {
        await using var db = DbContextFactory.Create(nameof(GetMatchReplays_ShouldReturnAllGoalTypesForExternalShare));

        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var baseTime = DateTimeOffset.UtcNow;
        db.ReplayClips.AddRange(
            MakeClip(groupId, match.Id, "gol-legado.mp4", MatchEventType.Gol, baseTime),
            MakeClip(groupId, match.Id, "gol-a.mp4", MatchEventType.GolTimeA, baseTime.AddSeconds(1)),
            MakeClip(groupId, match.Id, "gol-b.mp4", MatchEventType.GolTimeB, baseTime.AddSeconds(2)),
            MakeClip(groupId, match.Id, "jogada.mp4", MatchEventType.Jogada, baseTime.AddSeconds(3))
        );
        await db.SaveChangesAsync();

        var urls = new Mock<IReplayUrlService>();
        urls.Setup(u => u.GeneratePresignedUrl(It.IsAny<string>()))
            .Returns<string>(key => $"https://cdn.example/{key}");

        var sut = new PublicReplaysController(db, urls.Object);

        var result = await sut.GetMatchReplays(match.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<PublicMatchReplaysDto>().Subject;

        dto.Clips.Should().HaveCount(4);
        dto.Clips.Select(c => c.EventType)
            .Should().ContainInOrder("Gol", "GolTimeA", "GolTimeB", "Jogada");

        var goals = dto.Clips.Where(c => c.EventType.StartsWith("Gol")).ToList();
        goals.Should().HaveCount(3);
        goals.Select(c => c.GoalNumber).Should().Equal(1, 2, 3);
        goals.Select(c => c.TotalGoals).Should().OnlyContain(total => total == 3);
    }
}
