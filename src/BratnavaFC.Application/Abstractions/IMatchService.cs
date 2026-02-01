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
    Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals, bool randomize = false, CancellationToken cancellationToken = default);
}