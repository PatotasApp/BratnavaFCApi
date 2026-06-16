using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.TeamBuilder;

namespace BratnavaFC.Application.Abstractions;

public interface ITeamBuilderService
{
    Task<Result<TeamBuilderStatsDto>> GetStatsAsync(
        Guid groupId, List<Guid> playerIds, CancellationToken ct);
}
