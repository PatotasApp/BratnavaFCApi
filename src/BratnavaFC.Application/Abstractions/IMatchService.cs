using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchService
{
    Task<IEnumerable<MatchEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MatchEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MatchEntity> Create(MatchEntity match, CancellationToken cancellationToken);
    Task UpdateAsync(Guid matchId, UpdateMatchDto dto, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task AcceptInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken);
    Task RejectInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken);

    Task StartMatchAsync(Guid matchId, CancellationToken cancellationToken);
    Task EndMatchAsync(Guid matchId, CancellationToken cancellationToken);

    Task VoteAsync(Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken cancellationToken);
    Task<MatchPlayerEntity?> GetMvpAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken);
    Task SetTeamColorsAsync(Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken cancellationToken);

    Task FinalizeMatchAsync(Guid matchId, CancellationToken cancellationToken);
}