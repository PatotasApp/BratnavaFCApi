using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Retrospective;

namespace BratnavaFC.Application.Abstractions;

public interface IPlayerRetrospectiveService
{
    /// <summary>
    /// Retrospectiva do jogador no período: um ano (Brasília; padrão = o ano atual, ou o último
    /// com jogos) ou as datas <paramref name="from"/>–<paramref name="to"/> (inclusivas, Brasília).
    /// <paramref name="withPlayerList"/> devolve também quem jogou no período (para o admin escolher).
    /// </summary>
    Task<Result<PlayerRetrospectiveDto>> GetAsync(
        Guid groupId, Guid playerId, int? year, DateOnly? from, DateOnly? to, bool withPlayerList,
        CancellationToken ct = default);
}
