namespace BratnavaFC.Application.Abstractions;

public interface IMatchSchedulerJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}
