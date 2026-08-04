namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Push de eventos em tempo real para os clientes conectados.
///
/// Vive em Application, e não em Api junto da implementação SignalR, porque quem precisa
/// emitir não está só nos controllers: o <c>PushService</c> cria as notificações do sininho,
/// e os jobs recorrentes avançam status de partida sem passar por request nenhum. Application
/// não referencia Api, então a interface tem que morar aqui para esses dois alcançarem.
/// </summary>
public interface IRealtimeNotifier
{
    Task MatchChangedAsync(Guid groupId, Guid matchId, string reason, CancellationToken ct = default);

    Task PollChangedAsync(Guid groupId, Guid pollId, string reason, CancellationToken ct = default);

    Task GroupChangedAsync(Guid groupId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Avisa UM usuário que chegou notificação nova no sininho.
    ///
    /// Não carrega a contagem de não lidas de propósito: calcular exigiria um COUNT por
    /// usuário do lote, e o objetivo desta mudança é exatamente parar de tocar o banco para
    /// o compute do Neon poder suspender. O cliente incrementa o contador local — simétrico
    /// ao decremento que ele já faz sozinho ao marcar como lida.
    /// </summary>
    Task NotificationCreatedAsync(
        Guid userId,
        Guid? groupId,
        string title,
        string? notificationType,
        CancellationToken ct = default);
}
