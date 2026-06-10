namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Gerencia agendamentos Hangfire de notificações por push para partidas, votações e eventos.
/// Todas as datas de partida e eventos de calendário são tratadas como horário de Brasília (UTC-3).
/// </summary>
public interface INotificationScheduler
{
    // ── Partidas ────────────────────────────────────────────────────────────

    /// <summary>Agenda lembretes 24h e 2h antes de <paramref name="playedAt"/> (hora de Brasília).</summary>
    Task ScheduleMatchRemindersAsync(Guid matchId, Guid groupId, DateTime playedAt,
        CancellationToken ct = default);

    /// <summary>Cancela os agendamentos anteriores e cria novos com a data atualizada.</summary>
    Task RescheduleMatchRemindersAsync(Guid matchId, Guid groupId, DateTime newPlayedAt,
        CancellationToken ct = default);

    /// <summary>Cancela todos os agendamentos de lembrete de uma partida.</summary>
    Task CancelMatchRemindersAsync(Guid matchId, CancellationToken ct = default);

    // ── Votações/Eventos de poll ─────────────────────────────────────────────

    /// <summary>
    /// Agenda lembretes 24h e 2h antes do prazo, mais um job de fechamento automático no prazo.
    /// As datas são tratadas como horário de Brasília (UTC-3).
    /// Não faz nada se <paramref name="deadlineDate"/> for nulo.
    /// </summary>
    Task SchedulePollRemindersAsync(Guid pollId, Guid groupId, string pollTitle,
        DateOnly? deadlineDate, TimeOnly? deadlineTime, CancellationToken ct = default);

    /// <summary>Cancela e reagenda após alteração de prazo.</summary>
    Task ReschedulePollRemindersAsync(Guid pollId, Guid groupId, string pollTitle,
        DateOnly? deadlineDate, TimeOnly? deadlineTime, CancellationToken ct = default);

    /// <summary>Cancela todos os agendamentos de uma votação (inclui o job de fechamento).</summary>
    Task CancelPollRemindersAsync(Guid pollId, CancellationToken ct = default);

    // ── Sem quórum ───────────────────────────────────────────────────────────

    /// <summary>Agenda um lembrete para admins 3h antes da partida se o quórum não for atingido.</summary>
    Task ScheduleMatchNoQuorumReminderAsync(Guid matchId, Guid groupId, DateTime playedAt,
        CancellationToken ct = default);

    /// <summary>Cancela e reagenda o lembrete de quórum após alteração de data.</summary>
    Task RescheduleMatchNoQuorumReminderAsync(Guid matchId, Guid groupId, DateTime newPlayedAt,
        CancellationToken ct = default);

    /// <summary>Cancela o lembrete de quórum de uma partida.</summary>
    Task CancelMatchNoQuorumReminderAsync(Guid matchId, CancellationToken ct = default);

    // ── MVP automático ────────────────────────────────────────────────────────

    /// <summary>
    /// Agenda lembrete de votação MVP (1h antes) e finalização automática da partida.
    /// <paramref name="autoFinalizeHours"/> horas após o momento atual.
    /// </summary>
    Task ScheduleMvpAutoFinalizeAsync(Guid matchId, Guid groupId, int autoFinalizeHours,
        CancellationToken ct = default);

    /// <summary>Cancela os jobs de lembrete e finalização automática do MVP.</summary>
    Task CancelMvpAutoFinalizeAsync(Guid matchId, CancellationToken ct = default);

    // ── Eventos de calendário ────────────────────────────────────────────────

    /// <summary>
    /// Agenda lembretes 24h e 2h antes do evento.
    /// Não faz nada se <paramref name="eventTime"/> for nulo (horário em aberto).
    /// As datas são tratadas como horário de Brasília (UTC-3).
    /// </summary>
    Task ScheduleCalendarRemindersAsync(Guid eventId, Guid groupId, string title,
        DateOnly eventDate, TimeOnly? eventTime, CancellationToken ct = default);

    /// <summary>Cancela e reagenda após alteração de data/hora do evento.</summary>
    Task RescheduleCalendarRemindersAsync(Guid eventId, Guid groupId, string title,
        DateOnly eventDate, TimeOnly? eventTime, CancellationToken ct = default);

    /// <summary>Cancela todos os agendamentos de um evento de calendário.</summary>
    Task CancelCalendarRemindersAsync(Guid eventId, CancellationToken ct = default);
}
