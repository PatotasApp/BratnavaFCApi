using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

public interface IPlayerStatsService
{
    Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default);
    Task<PlayerVisualStatsReport>  GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<PlayerSpotlightReport>   GetSpotlightReportAsync(Guid groupId, CancellationToken cancellationToken = default);
}
