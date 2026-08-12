namespace BratnavaFC.Application.Abstractions;

public interface IConquistaProjectionService
{
    Task EnsureGroupProjectedAsync(Guid groupId, CancellationToken ct = default);
    Task ProjectMatchAsync(Guid groupId, Guid matchId, CancellationToken ct = default);
    Task RebuildGroupAsync(Guid groupId, CancellationToken ct = default);
}
