using BratnavaFC.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace BratnavaFC.Api.Realtime;

public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<RealtimeHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<RealtimeHub> hub)
    {
        _hub = hub;
    }

    public Task MatchChangedAsync(Guid groupId, Guid matchId, string reason, CancellationToken ct = default) =>
        SendAsync(groupId, new RealtimeEventDto(
            Type: "match.changed",
            GroupId: groupId,
            MatchId: matchId,
            PollId: null,
            Reason: reason,
            OccurredAtUtc: DateTime.UtcNow), ct);

    public Task PollChangedAsync(Guid groupId, Guid pollId, string reason, CancellationToken ct = default) =>
        SendAsync(groupId, new RealtimeEventDto(
            Type: "poll.changed",
            GroupId: groupId,
            MatchId: null,
            PollId: pollId,
            Reason: reason,
            OccurredAtUtc: DateTime.UtcNow), ct);

    public Task GroupChangedAsync(Guid groupId, string reason, CancellationToken ct = default) =>
        SendAsync(groupId, new RealtimeEventDto(
            Type: "group.changed",
            GroupId: groupId,
            MatchId: null,
            PollId: null,
            Reason: reason,
            OccurredAtUtc: DateTime.UtcNow), ct);

    /// <summary>
    /// Entrega por usuário, não por grupo: notificação de sininho é pessoal. Não precisa de
    /// JoinGroup — o <see cref="SubClaimUserIdProvider"/> resolve o destinatário a partir da
    /// claim do JWT, então o SignalR entrega a todas as conexões daquele usuário (várias abas,
    /// celular e desktop) sem registro nenhum de canal.
    /// </summary>
    public Task NotificationCreatedAsync(
        Guid userId,
        Guid? groupId,
        string title,
        string? notificationType,
        CancellationToken ct = default)
        => _hub.Clients.User(userId.ToString()).SendAsync(
            "NotificationEvent",
            new RealtimeNotificationDto(
                Type: "notification.created",
                GroupId: groupId,
                Title: title,
                NotificationType: notificationType,
                OccurredAtUtc: DateTime.UtcNow),
            ct);

    private Task SendAsync(Guid groupId, RealtimeEventDto payload, CancellationToken ct)
    {
        var channel = RealtimeHub.GroupChannel(groupId);
        return _hub.Clients.Group(channel).SendAsync("RealtimeEvent", payload, ct);
    }
}
