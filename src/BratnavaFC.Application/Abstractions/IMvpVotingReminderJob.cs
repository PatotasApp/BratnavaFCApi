namespace BratnavaFC.Application.Abstractions;

public interface IMvpVotingReminderJob
{
    Task ExecuteAsync(Guid matchId, Guid groupId, CancellationToken ct = default);
}
