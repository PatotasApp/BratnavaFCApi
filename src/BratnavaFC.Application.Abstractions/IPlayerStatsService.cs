using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.Abstractions;

public interface IPlayerStatsService
{
    Task<Result<List<PlayerStats>>> EnrichPlayersAsync(List<Player> players, CancellationToken cancellationToken = default);
}
