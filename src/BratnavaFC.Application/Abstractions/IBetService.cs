using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface IBetService
{
    Task<CurrentMatchBetContextDto?>   GetCurrentContextAsync(Guid groupId, Guid userId, CancellationToken ct);
    /// <summary>Returns the bet context for a specific match (used by the multi-match carousel).</summary>
    Task<CurrentMatchBetContextDto?>   GetContextForMatchAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken ct);
    /// <summary>
    /// Returns matches eligible for betting: MatchMaking status with at least one player
    /// assigned to each team. These are the matches shown in the bet carousel.
    /// </summary>
    Task<List<BettableMatchDto>>       GetBettableMatchesAsync(Guid groupId, CancellationToken ct);
    Task<Result>                       PlaceOrUpdateBetAsync(Guid groupId, Guid matchId, Guid userId, PlaceMatchBetDto dto, CancellationToken ct);
    Task<MatchBetResultsDto?>          GetMatchResultsAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<List<MatchBetHistoryDto>>     GetHistoryAsync(Guid groupId, CancellationToken ct);
    Task<List<BetLeaderboardEntryDto>> GetLeaderboardAsync(Guid groupId, CancellationToken ct);
    Task<int>                          GetMyBalanceAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result>                       DeleteBetAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken ct);
    Task<BetPreviewDto?>               GetBetPreviewAsync(Guid groupId, Guid matchId, CancellationToken ct);
    /// <summary>Resolve todas as apostas não resolvidas de uma partida já finalizada.</summary>
    Task ResolveMatchBetsAsync(Guid matchId, CancellationToken ct);
    /// <summary>Reverte a resolução anterior e re-resolve com o placar atual. Usar quando gols são alterados após finalização.</summary>
    Task ReResolveMatchBetsAsync(Guid matchId, CancellationToken ct);
    /// <summary>Recalcula todos os saldos do zero a partir das seleções resolvidas (endpoint temporário).</summary>
    Task<int> RecalculateAllBalancesAsync(CancellationToken ct);
    /// <summary>
    /// Removes all unresolved bets for the given match.
    /// Called automatically when teams are reassigned so stale bets are cleared.
    /// </summary>
    Task ResetBetsForMatchAsync(Guid matchId, CancellationToken ct);
}
