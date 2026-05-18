namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Job Hangfire recorrente (diário) que notifica todos os membros de um grupo
/// quando algum jogador do grupo faz aniversário no dia.
/// </summary>
public interface IBirthdayNotificationJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}
