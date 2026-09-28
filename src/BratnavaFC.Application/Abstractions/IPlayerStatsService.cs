using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

public interface IPlayerStatsService
{
    Task<Result<List<PlayerStats>>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default);
    Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, bool includeGuests = true, CancellationToken cancellationToken = default);
    Task<PlayerSpotlightReport> GetSpotlightReportAsync(Guid groupId, CancellationToken cancellationToken = default);
}
