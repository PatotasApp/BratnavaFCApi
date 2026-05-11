namespace BratnavaFC.Application.Abstractions;

public interface IClipCleanupJob
{
    Task ExecuteAsync(CancellationToken ct);
}
