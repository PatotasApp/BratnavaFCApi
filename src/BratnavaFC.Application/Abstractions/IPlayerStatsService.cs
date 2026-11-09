using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

public interface IPlayerStatsService
{
    Task<Result<List<PlayerStats>>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default);
    Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, bool includeGuests = true, CancellationToken cancellationToken = default);

    /// <summary>Relatório só com os jogos do período (temporada ou ano); <c>null</c> = tudo.</summary>
    Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, bool includeGuests, StatPeriod? period, CancellationToken cancellationToken = default) =>
        GetVisualReportAsync(groupId, includeGuests, cancellationToken);
    Task<PlayerSpotlightReport> GetSpotlightReportAsync(Guid groupId, CancellationToken cancellationToken = default);
}
