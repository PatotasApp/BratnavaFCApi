using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface IPuskasService
{
    Task<Result<PuskasDto>> GetAsync(Guid groupId, int top, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Informa (ou limpa, com playerId nulo) o autor do gol do clipe.</summary>
    Task<Result> SetScorerAsync(Guid groupId, Guid clipId, Guid? playerId, CancellationToken ct);
}
