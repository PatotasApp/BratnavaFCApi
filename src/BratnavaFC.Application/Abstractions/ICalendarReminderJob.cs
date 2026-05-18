namespace BratnavaFC.Application.Abstractions;

public interface ICalendarReminderJob
{
    /// <param name="triggerType">"24h" ou "2h"</param>
    Task ExecuteAsync(Guid eventId, Guid groupId, string triggerType, CancellationToken ct = default);
}
