using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchEventPublisher
{
    Task PublishAsync(Guid groupId, Guid matchId, MatchEventType type, DateTime eventTime, int secondsBeforeStart, int durationSeconds, CancellationToken ct = default);
}
