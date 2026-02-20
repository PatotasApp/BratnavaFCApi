using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchService
{
    Task<IEnumerable<MatchEntity>> GetAllAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<MatchEntity?> GetByIdAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default);

    Task<MatchEntity> Create(Guid groupId, MatchEntity match, CancellationToken cancellationToken);

    Task UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken cancellationToken);
    Task DeleteAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task SyncPlayersFromGroupAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task AcceptInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken);
    Task RejectInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken);

    Task StartMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);
    Task EndMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task VoteAsync(Guid groupId, Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken cancellationToken);
    Task<MatchPlayerEntity?> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default);

    Task SetScoreAsync(Guid groupId, Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken);
    Task SetTeamColorsAsync(Guid groupId, Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken cancellationToken);

    Task FinalizeMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);
    Task<MatchDetailsDto?> GetDetailsAsync(Guid matchId, CancellationToken ct);

    Task AssignTeamsAsync(Guid groupId, Guid matchId, AssignTeamsDto dto, CancellationToken ct);

    Task SwapPlayersByPlayerIdAsync(Guid groupId, Guid matchId, Guid playerAId, Guid playerBId, CancellationToken ct);

    Task AddGoalAsync(Guid groupId, Guid matchId, AddGoalRequestDto dto, CancellationToken ct);
    Task RemoveGoalAsync(Guid groupId, Guid matchId, Guid goalId, CancellationToken ct);
    Task AddGoalsBulkAsync(Guid groupId, Guid matchId, AddGoalsBulkRequestDto dto, CancellationToken ct);
    Task<List<GoalDto>> GetGoalsAsync(Guid groupId, Guid matchId, CancellationToken ct);
}
