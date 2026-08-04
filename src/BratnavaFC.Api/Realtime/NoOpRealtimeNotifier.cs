using BratnavaFC.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Api.Realtime;

/// <summary>
/// Registrado quando o SignalR está desabilitado no ambiente. O hub não é mapeado e o
/// AddSignalR não roda, então <see cref="SignalRRealtimeNotifier"/> não pode ser resolvido
/// — os controllers que injetam <see cref="IRealtimeNotifier"/> usam este no lugar.
/// </summary>
public sealed class NoOpRealtimeNotifier : IRealtimeNotifier
{
    private readonly ILogger<NoOpRealtimeNotifier> _logger;
    private readonly IWebHostEnvironment _environment;

    public NoOpRealtimeNotifier(
        ILogger<NoOpRealtimeNotifier> logger,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public Task MatchChangedAsync(Guid groupId, Guid matchId, string reason, CancellationToken ct = default)
        => SkippedAsync("match.changed", groupId, reason);

    public Task PollChangedAsync(Guid groupId, Guid pollId, string reason, CancellationToken ct = default)
        => SkippedAsync("poll.changed", groupId, reason);

    public Task GroupChangedAsync(Guid groupId, string reason, CancellationToken ct = default)
        => SkippedAsync("group.changed", groupId, reason);

    public Task NotificationCreatedAsync(
        Guid userId,
        Guid? groupId,
        string title,
        string? notificationType,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[Realtime] Evento de sininho ignorado porque o SignalR esta desabilitado no ambiente "
            + "{Environment}. User={UserId} Group={GroupId} Type={NotificationType}",
            _environment.EnvironmentName,
            userId,
            groupId,
            notificationType);

        return Task.CompletedTask;
    }

    private Task SkippedAsync(string eventType, Guid groupId, string reason)
    {
        _logger.LogDebug(
            "[Realtime] Evento ignorado porque o SignalR esta desabilitado no ambiente {Environment}. Type={Type} Group={GroupId} Reason={Reason}",
            _environment.EnvironmentName,
            eventType,
            groupId,
            reason);

        return Task.CompletedTask;
    }
}
