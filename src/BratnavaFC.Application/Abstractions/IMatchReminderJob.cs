namespace BratnavaFC.Application.Abstractions;

public interface IMatchReminderJob
{
    /// <param name="triggerType">"24h" ou "2h"</param>
    Task ExecuteAsync(Guid matchId, Guid groupId, string triggerType, CancellationToken ct = default);
}
