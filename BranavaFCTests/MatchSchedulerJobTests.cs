using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public sealed class MatchSchedulerJobTests
{
    [Fact]
    public async Task ExecuteAsync_WhenManualScheduleIsDue_ShouldCreateMatchAndMarkEntryCreated()
    {
        await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenManualScheduleIsDue_ShouldCreateMatchAndMarkEntryCreated));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var settings = new GroupSettingsEntity(group.Id, 5, 10, "Boca Jrs", DayOfWeek.Sunday, new TimeSpan(21, 0, 0));
        var playedAt = DateTime.SpecifyKind(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime.AddMinutes(-5),
            DateTimeKind.Utc);
        settings.SetMatchScheduling(
            enabled: true,
            mode: 0,
            scheduleDayOfWeek: null,
            scheduleTime: null,
            manualSchedules:
            [
                new ManualMatchScheduleEntry
                {
                    PlayedAt = playedAt,
                    Created = false,
                    MatchId = null,
                },
            ]);

        db.Groups.Add(group);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        MatchEntity? captured = null;
        var matchService = new Mock<IMatchService>();
        matchService
            .Setup(x => x.Create(group.Id, It.IsAny<MatchEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, MatchEntity match, CancellationToken _) =>
            {
                captured = match;
                return Result<MatchEntity>.Ok(match, "ok", ResultStatus.Created);
            });

        var sut = new MatchSchedulerJob(
            db,
            matchService.Object,
            Mock.Of<ILogger<MatchSchedulerJob>>());

        await sut.ExecuteAsync(CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.GroupId.Should().Be(group.Id);
        captured.PlaceName.Should().Be("Boca Jrs");
        captured.PlayedAt.Should().Be(playedAt);

        var saved = await db.GroupSettings.AsNoTracking().SingleAsync(x => x.GroupId == group.Id);
        var entry = saved.GetManualMatchSchedules().Single();
        entry.Created.Should().BeTrue();
        entry.MatchId.Should().Be(captured.Id);
    }
}
