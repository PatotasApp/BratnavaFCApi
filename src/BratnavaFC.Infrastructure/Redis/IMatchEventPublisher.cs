using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Infrastructure.Redis;

public interface IMatchEventPublisher
{
    Task<string> PublishAsync(Guid groupId, Guid matchId, MatchEventType type, int secondsBeforeStart, int durationSeconds, DateTimeOffset? eventTime = null, CancellationToken ct = default);
}
