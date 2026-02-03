using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchService
{
    Task<IEnumerable<MatchEntity>> GetAllAsync();
    Task<MatchEntity?> GetByIdAsync(Guid id);
    Task<MatchEntity> CreateAsync(MatchEntity match, CancellationToken cancellationToken);
    Task UpdateAsync(MatchEntity match, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task VoteAsync(Guid matchId, Guid voterPlayerId, Guid votedPlayerId, CancellationToken cancellationToken);
    Task FinalizeMatchAsync(Guid matchId, CancellationToken cancellationToken);
    Task<MatchPlayerEntity?> GetMvpAsync(Guid matchId);
    Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken);
    Task SetTeamColorsAsync(Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize = false, CancellationToken cancellationToken = default);
    Task AcceptInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken);
    Task RejectInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken);
}