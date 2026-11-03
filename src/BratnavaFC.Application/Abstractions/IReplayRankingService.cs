using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

/// <summary>Replays mais curtidos da patota, com colocação, placar, time e quem curtiu.</summary>
public interface IReplayRankingService
{
    /// <param name="goalsOnly">Só replays de gol (Gol, GolTimeA, GolTimeB).</param>
    /// <param name="from">Partidas a partir desta data (inclusive).</param>
    /// <param name="to">Partidas antes desta data (exclusive).</param>
    /// <param name="minLikes">Mínimo de curtidas (no mínimo 1).</param>
    /// <param name="top">Corte por colocação (0 = todos); empatados no corte entram.</param>
    Task<Result<ReplayRankingDto>> GetRankingAsync(
        Guid groupId, bool goalsOnly, DateTime? from, DateTime? to, int minLikes, int top, CancellationToken ct);
}
