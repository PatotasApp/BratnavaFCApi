using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface IBetService
{
    Task<CurrentMatchBetContextDto?>   GetCurrentContextAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result>                       PlaceOrUpdateBetAsync(Guid groupId, Guid matchId, Guid userId, PlaceMatchBetDto dto, CancellationToken ct);
    Task<MatchBetResultsDto?>          GetMatchResultsAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<List<MatchBetHistoryDto>>     GetHistoryAsync(Guid groupId, CancellationToken ct);
    Task<List<BetLeaderboardEntryDto>> GetLeaderboardAsync(Guid groupId, CancellationToken ct);
    Task<int>                          GetMyBalanceAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result>                       DeleteBetAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken ct);
    Task<BetPreviewDto?>               GetBetPreviewAsync(Guid groupId, Guid matchId, CancellationToken ct);
    /// <summary>Resolve todas as apostas não resolvidas de uma partida já finalizada.</summary>
    Task                               ResolveMatchBetsAsync(Guid matchId, CancellationToken ct);
}
