namespace BratnavaFC.Application.Abstractions;

public interface IFailedJobCleanupJob
{
    Task ExecuteAsync(CancellationToken ct);
}
