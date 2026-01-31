using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchService
{
    Task<IEnumerable<MatchEntity>> GetAllAsync();
    Task<MatchEntity?> GetByIdAsync(Guid id);
    Task<MatchEntity> CreateAsync(MatchEntity match);
    Task UpdateAsync(MatchEntity match);
    Task DeleteAsync(Guid id);
    Task VoteAsync(Guid matchId, Guid voterPlayerId, Guid votedPlayerId);
    Task FinalizeMatchAsync(Guid matchId);
    Task<MatchPlayerEntity?> GetMvpAsync(Guid matchId);
    Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals);
    Task SetTeamColorsAsync(Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize = false);
}