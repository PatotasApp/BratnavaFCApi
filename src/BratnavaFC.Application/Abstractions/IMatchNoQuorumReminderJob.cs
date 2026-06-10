namespace BratnavaFC.Application.Abstractions;

public interface IMatchNoQuorumReminderJob
{
    Task ExecuteAsync(Guid matchId, Guid groupId, CancellationToken ct = default);
}
