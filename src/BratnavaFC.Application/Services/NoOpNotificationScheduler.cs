using BratnavaFC.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class NoOpNotificationScheduler : INotificationScheduler
{
    private readonly ILogger<NoOpNotificationScheduler> _logger;

    public NoOpNotificationScheduler(ILogger<NoOpNotificationScheduler> logger)
    {
        _logger = logger;
    }

    public Task ScheduleMatchRemindersAsync(Guid matchId, Guid groupId, DateTime playedAt, CancellationToken ct = default)
        => SkippedAsync("match reminders", matchId);

    public Task RescheduleMatchRemindersAsync(Guid matchId, Guid groupId, DateTime newPlayedAt, CancellationToken ct = default)
        => SkippedAsync("match reminders reschedule", matchId);

    public Task CancelMatchRemindersAsync(Guid matchId, CancellationToken ct = default)
        => SkippedAsync("match reminders cancel", matchId);

    public Task SchedulePollRemindersAsync(Guid pollId, Guid groupId, string pollTitle, DateOnly? deadlineDate, TimeOnly? deadlineTime, CancellationToken ct = default)
        => SkippedAsync("poll reminders", pollId);

    public Task ReschedulePollRemindersAsync(Guid pollId, Guid groupId, string pollTitle, DateOnly? deadlineDate, TimeOnly? deadlineTime, CancellationToken ct = default)
        => SkippedAsync("poll reminders reschedule", pollId);

    public Task CancelPollRemindersAsync(Guid pollId, CancellationToken ct = default)
        => SkippedAsync("poll reminders cancel", pollId);

    public Task ScheduleMatchNoQuorumReminderAsync(Guid matchId, Guid groupId, DateTime playedAt, CancellationToken ct = default)
        => SkippedAsync("match no quorum reminder", matchId);

    public Task RescheduleMatchNoQuorumReminderAsync(Guid matchId, Guid groupId, DateTime newPlayedAt, CancellationToken ct = default)
        => SkippedAsync("match no quorum reminder reschedule", matchId);

    public Task CancelMatchNoQuorumReminderAsync(Guid matchId, CancellationToken ct = default)
        => SkippedAsync("match no quorum reminder cancel", matchId);

    public Task ScheduleMvpAutoFinalizeAsync(Guid matchId, Guid groupId, int autoFinalizeHours, CancellationToken ct = default)
        => SkippedAsync("mvp auto finalize", matchId);

    public Task CancelMvpAutoFinalizeAsync(Guid matchId, CancellationToken ct = default)
        => SkippedAsync("mvp auto finalize cancel", matchId);

    public Task ScheduleCalendarRemindersAsync(Guid eventId, Guid groupId, string title, DateOnly eventDate, TimeOnly? eventTime, CancellationToken ct = default)
        => SkippedAsync("calendar reminders", eventId);

    public Task RescheduleCalendarRemindersAsync(Guid eventId, Guid groupId, string title, DateOnly eventDate, TimeOnly? eventTime, CancellationToken ct = default)
        => SkippedAsync("calendar reminders reschedule", eventId);

    public Task CancelCalendarRemindersAsync(Guid eventId, CancellationToken ct = default)
        => SkippedAsync("calendar reminders cancel", eventId);

    private Task SkippedAsync(string operation, Guid entityId)
    {
        _logger.LogWarning(
            "[Hangfire] Agendamento ignorado porque o storage está indisponível. Operation={Operation} EntityId={EntityId}",
            operation,
            entityId);

        return Task.CompletedTask;
    }
}
