namespace BratnavaFC.Application.Abstractions;

public interface IMatchAutoFinalizeJob
{
    Task ExecuteAsync(Guid matchId, Guid groupId, CancellationToken ct = default);
}
