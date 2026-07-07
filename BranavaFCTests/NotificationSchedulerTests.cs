using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class NotificationSchedulerTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Mock<IBackgroundJobClient> JobsMock()
    {
        var jobs = new Mock<IBackgroundJobClient>();
        var counter = 0;

        // Hangfire's Schedule<T> extension methods funnel into Create(Job, IState).
        jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns(() => $"job-{Interlocked.Increment(ref counter)}");

        // Hangfire's Delete extension funnels into ChangeState(jobId, DeletedState, fromState).
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        return jobs;
    }

    private static NotificationScheduler Sut(AppDbContext db, IBackgroundJobClient jobs)
        => new(db, jobs, Mock.Of<ILogger<NotificationScheduler>>());

    /// <summary>UTC "now" expressed as a naive Brasília (UTC-3) wall-clock DateTime.</summary>
    private static DateTime BrasilNow() => DateTime.UtcNow.AddHours(-3);

    // ─── ScheduleMatchRemindersAsync ─────────────────────────────────────────

    [Fact]
    public async Task ScheduleMatchReminders_WhenFarInFuture_CreatesBothTriggers()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchReminders_WhenFarInFuture_CreatesBothTriggers));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMatchRemindersAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(30));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.TriggerType).Should().BeEquivalentTo(new[] { "24h", "2h" });
        rows.Should().OnlyContain(r => r.EntityType == "match" && r.EntityId == matchId);
        rows.Select(r => r.HangfireJobId).Should().OnlyHaveUniqueItems();
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ScheduleMatchReminders_WhenOnlyTwoHourWindowRemains_CreatesOnlyTwoHourTrigger()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchReminders_WhenOnlyTwoHourWindowRemains_CreatesOnlyTwoHourTrigger));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        // Match in ~3h → the 24h trigger is in the past, the 2h trigger is ~1h away.
        await sut.ScheduleMatchRemindersAsync(Guid.NewGuid(), Guid.NewGuid(), BrasilNow().AddHours(3));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().ContainSingle().Which.TriggerType.Should().Be("2h");
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task ScheduleMatchReminders_WhenMatchIsInThePast_SchedulesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchReminders_WhenMatchIsInThePast_SchedulesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        await sut.ScheduleMatchRemindersAsync(Guid.NewGuid(), Guid.NewGuid(), BrasilNow().AddHours(-1));

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task ScheduleMatchReminders_WithinMinLeadMargin_SkipsTrigger()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchReminders_WithinMinLeadMargin_SkipsTrigger));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        // Match in 2h03m → the 2h trigger would fire in 3 minutes (< 5min MinLead) → skipped.
        await sut.ScheduleMatchRemindersAsync(Guid.NewGuid(), Guid.NewGuid(), BrasilNow().AddHours(2).AddMinutes(3));

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    // ─── CancelMatchRemindersAsync ───────────────────────────────────────────

    [Fact]
    public async Task CancelMatchReminders_RemovesRows_AndDeletesHangfireJobs()
    {
        await using var db = DbContextFactory.Create(nameof(CancelMatchReminders_RemovesRows_AndDeletesHangfireJobs));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMatchRemindersAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(48));
        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(2);

        await sut.CancelMatchRemindersAsync(matchId);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CancelMatchReminders_WhenNoJobsExist_DoesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(CancelMatchReminders_WhenNoJobsExist_DoesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        await sut.CancelMatchRemindersAsync(Guid.NewGuid());

        jobs.Verify(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancelMatchReminders_WhenHangfireDeleteThrows_StillRemovesRows()
    {
        await using var db = DbContextFactory.Create(nameof(CancelMatchReminders_WhenHangfireDeleteThrows_StillRemovesRows));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMatchRemindersAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(48));

        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("job already executed"));

        await sut.CancelMatchRemindersAsync(matchId);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0,
            "rows must be removed even when the Hangfire delete fails");
    }

    [Fact]
    public async Task CancelMatchReminders_DoesNotTouchJobsOfOtherEntities()
    {
        await using var db = DbContextFactory.Create(nameof(CancelMatchReminders_DoesNotTouchJobsOfOtherEntities));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchA = Guid.NewGuid();
        var matchB = Guid.NewGuid();

        await sut.ScheduleMatchRemindersAsync(matchA, Guid.NewGuid(), BrasilNow().AddHours(48));
        await sut.ScheduleMatchRemindersAsync(matchB, Guid.NewGuid(), BrasilNow().AddHours(48));

        await sut.CancelMatchRemindersAsync(matchA);

        var remaining = await db.ScheduledNotificationJobs.ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().OnlyContain(r => r.EntityId == matchB);
    }

    // ─── RescheduleMatchRemindersAsync ───────────────────────────────────────

    [Fact]
    public async Task RescheduleMatchReminders_ReplacesExistingJobs()
    {
        await using var db = DbContextFactory.Create(nameof(RescheduleMatchReminders_ReplacesExistingJobs));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMatchRemindersAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(48));
        var oldIds = await db.ScheduledNotificationJobs.Select(r => r.HangfireJobId).ToListAsync();

        await sut.RescheduleMatchRemindersAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(72));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.HangfireJobId).Should().NotIntersectWith(oldIds);
        jobs.Verify(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()), Times.Exactly(2));
    }

    // ─── No-quorum reminder ──────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleMatchNoQuorumReminder_WhenFarEnough_CreatesRow()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchNoQuorumReminder_WhenFarEnough_CreatesRow));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        // Fires 3h before the match → match in 5h leaves a 2h lead.
        await sut.ScheduleMatchNoQuorumReminderAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(5));

        var row = (await db.ScheduledNotificationJobs.ToListAsync()).Should().ContainSingle().Subject;
        row.EntityType.Should().Be("match_noquorum");
        row.TriggerType.Should().Be("noquorum");
        row.EntityId.Should().Be(matchId);
    }

    [Fact]
    public async Task ScheduleMatchNoQuorumReminder_WhenTooClose_SchedulesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchNoQuorumReminder_WhenTooClose_SchedulesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        // Match in 3h → fireAt would be right now (< MinLead).
        await sut.ScheduleMatchNoQuorumReminderAsync(Guid.NewGuid(), Guid.NewGuid(), BrasilNow().AddHours(3));

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task RescheduleMatchNoQuorumReminder_CancelsAndRecreates()
    {
        await using var db = DbContextFactory.Create(nameof(RescheduleMatchNoQuorumReminder_CancelsAndRecreates));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMatchNoQuorumReminderAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(5));
        var oldId = (await db.ScheduledNotificationJobs.SingleAsync()).HangfireJobId;

        await sut.RescheduleMatchNoQuorumReminderAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(10));

        var row = await db.ScheduledNotificationJobs.SingleAsync();
        row.HangfireJobId.Should().NotBe(oldId);
    }

    [Fact]
    public async Task CancelMatchNoQuorumReminder_RemovesRow()
    {
        await using var db = DbContextFactory.Create(nameof(CancelMatchNoQuorumReminder_RemovesRow));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMatchNoQuorumReminderAsync(matchId, Guid.NewGuid(), BrasilNow().AddHours(5));
        await sut.CancelMatchNoQuorumReminderAsync(matchId);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
    }

    // ─── MVP auto-finalize ───────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleMvpAutoFinalize_WithLongWindow_CreatesReminderAndFinalize()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMvpAutoFinalize_WithLongWindow_CreatesReminderAndFinalize));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMvpAutoFinalizeAsync(matchId, Guid.NewGuid(), autoFinalizeHours: 24);

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.TriggerType).Should().BeEquivalentTo(new[] { "reminder", "finalize" });
        rows.Should().OnlyContain(r => r.EntityType == "mvp" && r.EntityId == matchId);
    }

    [Fact]
    public async Task ScheduleMvpAutoFinalize_WithOneHour_CreatesOnlyFinalize()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMvpAutoFinalize_WithOneHour_CreatesOnlyFinalize));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        // reminderAt = now + 1h - 1h = now → below MinLead, only the finalize job survives.
        await sut.ScheduleMvpAutoFinalizeAsync(Guid.NewGuid(), Guid.NewGuid(), autoFinalizeHours: 1);

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().ContainSingle().Which.TriggerType.Should().Be("finalize");
    }

    [Fact]
    public async Task ScheduleMvpAutoFinalize_WithZeroHours_SchedulesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMvpAutoFinalize_WithZeroHours_SchedulesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        await sut.ScheduleMvpAutoFinalizeAsync(Guid.NewGuid(), Guid.NewGuid(), autoFinalizeHours: 0);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task CancelMvpAutoFinalize_RemovesMvpRows()
    {
        await using var db = DbContextFactory.Create(nameof(CancelMvpAutoFinalize_RemovesMvpRows));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var matchId = Guid.NewGuid();

        await sut.ScheduleMvpAutoFinalizeAsync(matchId, Guid.NewGuid(), autoFinalizeHours: 24);
        await sut.CancelMvpAutoFinalizeAsync(matchId);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()), Times.Exactly(2));
    }

    // ─── Polls ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SchedulePollReminders_WhenNoDeadline_SchedulesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(SchedulePollReminders_WhenNoDeadline_SchedulesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        await sut.SchedulePollRemindersAsync(Guid.NewGuid(), Guid.NewGuid(), "Poll", null, null);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task SchedulePollReminders_WithFutureDeadline_CreatesRemindersAndCloseJob()
    {
        await using var db = DbContextFactory.Create(nameof(SchedulePollReminders_WithFutureDeadline_CreatesRemindersAndCloseJob));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var pollId = Guid.NewGuid();

        var deadlineLocal = BrasilNow().AddDays(3);
        await sut.SchedulePollRemindersAsync(
            pollId, Guid.NewGuid(), "Poll",
            DateOnly.FromDateTime(deadlineLocal), TimeOnly.FromDateTime(deadlineLocal));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(3);
        rows.Select(r => r.TriggerType).Should().BeEquivalentTo(new[] { "24h", "2h", "close" });
        rows.Should().OnlyContain(r => r.EntityType == "poll" && r.EntityId == pollId);
    }

    [Fact]
    public async Task SchedulePollReminders_WithoutTime_UsesEndOfDay_AndSchedules()
    {
        await using var db = DbContextFactory.Create(nameof(SchedulePollReminders_WithoutTime_UsesEndOfDay_AndSchedules));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var pollId = Guid.NewGuid();

        var deadlineDate = DateOnly.FromDateTime(BrasilNow().AddDays(5));
        await sut.SchedulePollRemindersAsync(pollId, Guid.NewGuid(), "Poll", deadlineDate, null);

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(3);

        // 23:59:59 Brasília = 02:59:59 UTC on the next day
        var close = rows.Single(r => r.TriggerType == "close");
        close.ScheduledForUtc.TimeOfDay.Should().Be(new TimeSpan(2, 59, 59));
    }

    [Fact]
    public async Task SchedulePollReminders_WithPastDeadline_SchedulesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(SchedulePollReminders_WithPastDeadline_SchedulesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        var past = BrasilNow().AddDays(-2);
        await sut.SchedulePollRemindersAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Poll",
            DateOnly.FromDateTime(past), TimeOnly.FromDateTime(past));

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task ReschedulePollReminders_ReplacesExistingJobs()
    {
        await using var db = DbContextFactory.Create(nameof(ReschedulePollReminders_ReplacesExistingJobs));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var pollId = Guid.NewGuid();

        var d1 = BrasilNow().AddDays(3);
        await sut.SchedulePollRemindersAsync(pollId, Guid.NewGuid(), "Poll",
            DateOnly.FromDateTime(d1), TimeOnly.FromDateTime(d1));
        var oldIds = await db.ScheduledNotificationJobs.Select(r => r.HangfireJobId).ToListAsync();

        var d2 = BrasilNow().AddDays(6);
        await sut.ReschedulePollRemindersAsync(pollId, Guid.NewGuid(), "Poll",
            DateOnly.FromDateTime(d2), TimeOnly.FromDateTime(d2));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(3);
        rows.Select(r => r.HangfireJobId).Should().NotIntersectWith(oldIds);
    }

    [Fact]
    public async Task CancelPollReminders_RemovesAllPollRows()
    {
        await using var db = DbContextFactory.Create(nameof(CancelPollReminders_RemovesAllPollRows));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var pollId = Guid.NewGuid();

        var d = BrasilNow().AddDays(3);
        await sut.SchedulePollRemindersAsync(pollId, Guid.NewGuid(), "Poll",
            DateOnly.FromDateTime(d), TimeOnly.FromDateTime(d));

        await sut.CancelPollRemindersAsync(pollId);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()), Times.Exactly(3));
    }

    // ─── Calendar events ─────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleCalendarReminders_WhenTimeTbd_SchedulesNothing()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleCalendarReminders_WhenTimeTbd_SchedulesNothing));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        await sut.ScheduleCalendarRemindersAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Churrasco",
            DateOnly.FromDateTime(BrasilNow().AddDays(3)), eventTime: null);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task ScheduleCalendarReminders_WithFutureEvent_CreatesBothTriggers()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleCalendarReminders_WithFutureEvent_CreatesBothTriggers));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var eventId = Guid.NewGuid();

        var eventLocal = BrasilNow().AddDays(3);
        await sut.ScheduleCalendarRemindersAsync(
            eventId, Guid.NewGuid(), "Churrasco",
            DateOnly.FromDateTime(eventLocal), TimeOnly.FromDateTime(eventLocal));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.TriggerType).Should().BeEquivalentTo(new[] { "24h", "2h" });
        rows.Should().OnlyContain(r => r.EntityType == "calendar" && r.EntityId == eventId);
    }

    [Fact]
    public async Task RescheduleCalendarReminders_ReplacesExistingJobs()
    {
        await using var db = DbContextFactory.Create(nameof(RescheduleCalendarReminders_ReplacesExistingJobs));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var eventId = Guid.NewGuid();

        var d1 = BrasilNow().AddDays(3);
        await sut.ScheduleCalendarRemindersAsync(eventId, Guid.NewGuid(), "Evento",
            DateOnly.FromDateTime(d1), TimeOnly.FromDateTime(d1));
        var oldIds = await db.ScheduledNotificationJobs.Select(r => r.HangfireJobId).ToListAsync();

        var d2 = BrasilNow().AddDays(7);
        await sut.RescheduleCalendarRemindersAsync(eventId, Guid.NewGuid(), "Evento",
            DateOnly.FromDateTime(d2), TimeOnly.FromDateTime(d2));

        var rows = await db.ScheduledNotificationJobs.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.HangfireJobId).Should().NotIntersectWith(oldIds);
    }

    [Fact]
    public async Task CancelCalendarReminders_RemovesRows()
    {
        await using var db = DbContextFactory.Create(nameof(CancelCalendarReminders_RemovesRows));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);
        var eventId = Guid.NewGuid();

        var d = BrasilNow().AddDays(3);
        await sut.ScheduleCalendarRemindersAsync(eventId, Guid.NewGuid(), "Evento",
            DateOnly.FromDateTime(d), TimeOnly.FromDateTime(d));

        await sut.CancelCalendarRemindersAsync(eventId);

        (await db.ScheduledNotificationJobs.CountAsync()).Should().Be(0);
    }

    // ─── Timezone conversion ─────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleMatchReminders_TreatsPlayedAtAsBrasiliaTime()
    {
        await using var db = DbContextFactory.Create(nameof(ScheduleMatchReminders_TreatsPlayedAtAsBrasiliaTime));
        var jobs = JobsMock();
        var sut = Sut(db, jobs.Object);

        var playedAtLocal = BrasilNow().AddHours(48);
        await sut.ScheduleMatchRemindersAsync(Guid.NewGuid(), Guid.NewGuid(), playedAtLocal);

        var expectedTargetUtc = playedAtLocal.AddHours(3); // UTC-3 → UTC
        var row24 = await db.ScheduledNotificationJobs.SingleAsync(r => r.TriggerType == "24h");
        row24.ScheduledForUtc.Should().BeCloseTo(expectedTargetUtc.AddHours(-24), TimeSpan.FromSeconds(1));
    }
}
