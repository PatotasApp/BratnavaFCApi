using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class NotificationScheduler : INotificationScheduler
{
    // Brasil aboliu o horário de verão em 2019 — offset permanentemente UTC-3.
    private static readonly TimeSpan BrasilOffset = TimeSpan.FromHours(-3);

    // Margem mínima: não agenda se o disparo seria em menos de 5 minutos.
    private static readonly TimeSpan MinLead = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<NotificationScheduler> _logger;

    public NotificationScheduler(
        AppDbContext db,
        IBackgroundJobClient jobs,
        ILogger<NotificationScheduler> logger)
    {
        _db     = db;
        _jobs   = jobs;
        _logger = logger;
    }

    // ── Partidas ─────────────────────────────────────────────────────────────

    public async Task ScheduleMatchRemindersAsync(
        Guid matchId, Guid groupId, DateTime playedAt,
        CancellationToken ct = default)
    {
        // playedAt é tratado como horário de Brasília (UTC-3)
        var targetUtc = LocalBrasilToUtc(playedAt);
        var triggers  = BuildReminderTriggers(targetUtc);

        foreach (var (type, fireAt) in triggers)
        {
            var jobId = _jobs.Schedule<IMatchReminderJob>(
                j => j.ExecuteAsync(matchId, groupId, type, CancellationToken.None),
                fireAt);

            _db.ScheduledNotificationJobs.Add(
                new ScheduledNotificationJobEntity("match", matchId, type, jobId, fireAt.UtcDateTime));

            _logger.LogInformation(
                "NotificationScheduler: agendado lembrete {Type} para partida {MatchId} em {FireAt:u}.",
                type, matchId, fireAt);
        }

        if (triggers.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    public async Task RescheduleMatchRemindersAsync(
        Guid matchId, Guid groupId, DateTime newPlayedAt,
        CancellationToken ct = default)
    {
        await CancelMatchRemindersAsync(matchId, ct);
        await ScheduleMatchRemindersAsync(matchId, groupId, newPlayedAt, ct);
    }

    public Task CancelMatchRemindersAsync(Guid matchId, CancellationToken ct = default)
        => CancelJobsAsync("match", matchId, ct);

    // ── Votações ─────────────────────────────────────────────────────────────

    public async Task SchedulePollRemindersAsync(
        Guid pollId, Guid groupId, string pollTitle,
        DateOnly? deadlineDate, TimeOnly? deadlineTime,
        CancellationToken ct = default)
    {
        if (deadlineDate is null) return;

        var deadlineUtc = DateOnlyToUtc(deadlineDate.Value, deadlineTime);
        var reminderTriggers = BuildReminderTriggers(deadlineUtc);
        var saved = false;

        foreach (var (type, fireAt) in reminderTriggers)
        {
            var jobId = _jobs.Schedule<IPollReminderJob>(
                j => j.ExecuteReminderAsync(pollId, groupId, type, CancellationToken.None),
                fireAt);

            _db.ScheduledNotificationJobs.Add(
                new ScheduledNotificationJobEntity("poll", pollId, type, jobId, fireAt.UtcDateTime));

            _logger.LogInformation(
                "NotificationScheduler: agendado lembrete {Type} para votação {PollId} em {FireAt:u}.",
                type, pollId, fireAt);
            saved = true;
        }

        // Job de fechamento automático exatamente no prazo
        var closeAt = new DateTimeOffset(deadlineUtc, TimeSpan.Zero);
        if (closeAt - DateTimeOffset.UtcNow > MinLead)
        {
            var closeJobId = _jobs.Schedule<IPollReminderJob>(
                j => j.ExecuteAutoCloseAsync(pollId, CancellationToken.None),
                closeAt);

            _db.ScheduledNotificationJobs.Add(
                new ScheduledNotificationJobEntity("poll", pollId, "close", closeJobId, closeAt.UtcDateTime));

            _logger.LogInformation(
                "NotificationScheduler: agendado fechamento automático para votação {PollId} em {CloseAt:u}.",
                pollId, closeAt);
            saved = true;
        }

        if (saved)
            await _db.SaveChangesAsync(ct);
    }

    public async Task ReschedulePollRemindersAsync(
        Guid pollId, Guid groupId, string pollTitle,
        DateOnly? deadlineDate, TimeOnly? deadlineTime,
        CancellationToken ct = default)
    {
        await CancelPollRemindersAsync(pollId, ct);
        await SchedulePollRemindersAsync(pollId, groupId, pollTitle, deadlineDate, deadlineTime, ct);
    }

    public Task CancelPollRemindersAsync(Guid pollId, CancellationToken ct = default)
        => CancelJobsAsync("poll", pollId, ct);

    // ── Eventos de calendário ────────────────────────────────────────────────

    public async Task ScheduleCalendarRemindersAsync(
        Guid eventId, Guid groupId, string title,
        DateOnly eventDate, TimeOnly? eventTime,
        CancellationToken ct = default)
    {
        // Sem horário definido (TimeTBD) — não é possível agendar lembrete preciso
        if (eventTime is null) return;

        var targetUtc = DateOnlyToUtc(eventDate, eventTime);
        var triggers  = BuildReminderTriggers(targetUtc);

        foreach (var (type, fireAt) in triggers)
        {
            var jobId = _jobs.Schedule<ICalendarReminderJob>(
                j => j.ExecuteAsync(eventId, groupId, type, CancellationToken.None),
                fireAt);

            _db.ScheduledNotificationJobs.Add(
                new ScheduledNotificationJobEntity("calendar", eventId, type, jobId, fireAt.UtcDateTime));

            _logger.LogInformation(
                "NotificationScheduler: agendado lembrete {Type} para evento {EventId} em {FireAt:u}.",
                type, eventId, fireAt);
        }

        if (triggers.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    public async Task RescheduleCalendarRemindersAsync(
        Guid eventId, Guid groupId, string title,
        DateOnly eventDate, TimeOnly? eventTime,
        CancellationToken ct = default)
    {
        await CancelCalendarRemindersAsync(eventId, ct);
        await ScheduleCalendarRemindersAsync(eventId, groupId, title, eventDate, eventTime, ct);
    }

    public Task CancelCalendarRemindersAsync(Guid eventId, CancellationToken ct = default)
        => CancelJobsAsync("calendar", eventId, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converte um DateTime sem fuso (tratado como Brasília UTC-3) para UTC.
    /// </summary>
    private static DateTime LocalBrasilToUtc(DateTime dt)
        => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), BrasilOffset)
            .UtcDateTime;

    /// <summary>
    /// Converte DateOnly + TimeOnly (Brasília UTC-3) para UTC.
    /// Sem TimeOnly, usa fim-do-dia (23:59:59).
    /// </summary>
    private static DateTime DateOnlyToUtc(DateOnly date, TimeOnly? time)
    {
        var localDt = date.ToDateTime(time ?? new TimeOnly(23, 59, 59));
        return new DateTimeOffset(localDt, BrasilOffset).UtcDateTime;
    }

    /// <summary>
    /// Retorna os gatilhos 24h e 2h antes de <paramref name="targetUtc"/>
    /// que ainda estejam suficientemente no futuro (> MinLead).
    /// </summary>
    private static List<(string type, DateTimeOffset fireAt)> BuildReminderTriggers(DateTime targetUtc)
    {
        var result = new List<(string, DateTimeOffset)>();
        var now    = DateTimeOffset.UtcNow;
        var target = new DateTimeOffset(targetUtc, TimeSpan.Zero);

        var at24h = target - TimeSpan.FromHours(24);
        if (at24h - now > MinLead) result.Add(("24h", at24h));

        var at2h = target - TimeSpan.FromHours(2);
        if (at2h - now > MinLead) result.Add(("2h", at2h));

        return result;
    }

    private async Task CancelJobsAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        var jobs = await _db.ScheduledNotificationJobs
            .Where(j => j.EntityType == entityType && j.EntityId == entityId)
            .ToListAsync(ct);

        if (jobs.Count == 0) return;

        foreach (var job in jobs)
        {
            try
            {
                _jobs.Delete(job.HangfireJobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "NotificationScheduler: não foi possível cancelar o job Hangfire {JobId} — já pode ter sido executado.",
                    job.HangfireJobId);
            }
        }

        _db.ScheduledNotificationJobs.RemoveRange(jobs);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "NotificationScheduler: {Count} job(s) cancelado(s) para {EntityType} {EntityId}.",
            jobs.Count, entityType, entityId);
    }
}
