using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.Abstractions
{
    public interface IPlayerStatsService
    {
        Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerEntity> players, CancellationToken cancellationToken = default);

        Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default);
    }
}
