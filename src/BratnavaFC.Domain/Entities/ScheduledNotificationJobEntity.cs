namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Rastreia os Hangfire job IDs dos agendamentos de notificação por push,
/// permitindo cancelar e reagendar quando a entidade relacionada for alterada ou excluída.
/// </summary>
public class ScheduledNotificationJobEntity : BaseEntity
{
    // EF
    private ScheduledNotificationJobEntity() { }

    public ScheduledNotificationJobEntity(
        string entityType,
        Guid   entityId,
        string triggerType,
        string hangfireJobId,
        DateTime scheduledForUtc)
    {
        EntityType     = entityType;
        EntityId       = entityId;
        TriggerType    = triggerType;
        HangfireJobId  = hangfireJobId;
        ScheduledForUtc = scheduledForUtc;
    }

    /// <summary>"match" | "poll" | "calendar"</summary>
    public string EntityType { get; private set; } = null!;

    /// <summary>Id da entidade relacionada (MatchId, PollId ou CalendarEventId).</summary>
    public Guid EntityId { get; private set; }

    /// <summary>"24h" | "2h" | "close"</summary>
    public string TriggerType { get; private set; } = null!;

    /// <summary>Job ID retornado pelo Hangfire ao agendar.</summary>
    public string HangfireJobId { get; private set; } = null!;

    /// <summary>Data/hora em UTC em que o job foi agendado para disparar.</summary>
    public DateTime ScheduledForUtc { get; private set; }
}
