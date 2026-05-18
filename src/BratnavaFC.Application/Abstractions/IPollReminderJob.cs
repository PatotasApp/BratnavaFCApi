namespace BratnavaFC.Application.Abstractions;

public interface IPollReminderJob
{
    /// <summary>Envia lembrete push para o grupo antes do prazo.</summary>
    /// <param name="triggerType">"24h" ou "2h"</param>
    Task ExecuteReminderAsync(Guid pollId, Guid groupId, string triggerType, CancellationToken ct = default);

    /// <summary>Fecha automaticamente a votação quando o prazo chega.</summary>
    Task ExecuteAutoCloseAsync(Guid pollId, CancellationToken ct = default);
}
