namespace BratnavaFC.Api.Realtime;

public interface IRealtimeNotifier
{
    Task MatchChangedAsync(Guid groupId, Guid matchId, string reason, CancellationToken ct = default);
    Task PollChangedAsync(Guid groupId, Guid pollId, string reason, CancellationToken ct = default);
    Task GroupChangedAsync(Guid groupId, string reason, CancellationToken ct = default);
}
