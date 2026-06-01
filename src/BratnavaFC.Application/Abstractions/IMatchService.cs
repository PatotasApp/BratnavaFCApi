using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchService
{
    Task<Result<List<MatchDetailsDto>>> GetAllAsync(Guid groupId, CancellationToken ct = default);
    Task<Result<MatchEntity>> GetByIdAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default);

    Task<Result<MatchEntity>> Create(Guid groupId, MatchEntity match, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task<Result> SyncPlayersFromGroupAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task<Result> AcceptInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken);
    Task<Result> RejectInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken);

    /// <summary>Aceita/rejeita convite resolvendo o playerId pelo userId do token JWT.</summary>
    Task<Result> AcceptMyInviteAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken cancellationToken);
    Task<Result> RejectMyInviteAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken cancellationToken);

    Task<Result> StartMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);
    Task<Result> EndMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task<Result> VoteAsync(Guid groupId, Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken cancellationToken);
    Task<Result<MatchPlayerEntity>> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default);

    Task<Result> SetScoreAsync(Guid groupId, Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken);
    Task<Result> SetTeamColorsAsync(Guid groupId, Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken cancellationToken);

    Task<Result> FinalizeMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);
    Task<Result> ReapplyMvpTieRuleAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken);

    Task<Result<MatchDetailsDto>> GetDetailsAsync(Guid matchId, CancellationToken ct);
    Task<Result> AssignTeamsAsync(Guid groupId, Guid matchId, AssignTeamsDto dto, CancellationToken ct);
    Task<Result> SwapPlayersByPlayerIdAsync(Guid groupId, Guid matchId, Guid playerAId, Guid playerBId, CancellationToken ct);
    Task<Result> SetPlayerRoleAsync(Guid groupId, Guid matchId, Guid matchPlayerId, SetPlayerRoleDto dto, CancellationToken ct);
    Task<Result> SetNoShowAsync(Guid groupId, Guid matchId, Guid matchPlayerId, bool didNotPlay, CancellationToken ct);
    Task<Result> AddGoalAsync(Guid groupId, Guid matchId, AddGoalRequestDto dto, CancellationToken ct);
    Task<Result> UpdateGoalAsync(Guid groupId, Guid matchId, Guid goalId, UpdateGoalRequestDto dto, CancellationToken ct);
    Task<Result> RemoveGoalAsync(Guid groupId, Guid matchId, Guid goalId, CancellationToken ct);
    Task<Result> AddGoalsBulkAsync(Guid groupId, Guid matchId, AddGoalsBulkRequestDto dto, CancellationToken ct);
    Task<Result<List<GoalDto>>> GetGoalsAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<Result> GoToMatchMakingAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<Result> GoToPostGameAsync(Guid groupId, Guid matchId, CancellationToken ct);

    Task<Result<MatchHeaderDto>> GetHeaderAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<Result<MatchAcceptationDto>> GetAcceptationAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<Result<MatchMatchMakingDto>> GetMatchMakingAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<Result<MatchPostGameDto>> GetPostGameAsync(Guid groupId, Guid matchId, CancellationToken ct, Guid? requestingUserId = null);

    Task<Result<MatchEntity>>           GetCurrentAsync(Guid groupId, CancellationToken ct);
    Task<Result<List<MatchHeaderDto>>> GetUpcomingAsync(Guid groupId, CancellationToken ct);

    /// <summary>
    /// Adds a single active player to every pre-game match (Created / Acceptation) of the group
    /// they do not already belong to. Called automatically when a player is created or reactivated.
    /// </summary>
    Task<Result> SyncPlayerIntoActiveMatchesAsync(Guid groupId, Guid playerId, CancellationToken ct);

    Task<Result> RewindOneStepAsync(Guid groupId, Guid matchId, CancellationToken ct);
    Task<Result<IReadOnlyList<MatchHistoryItemDto>>> GetHistoryAsync(Guid groupId, int take, int skip, CancellationToken cancellationToken, Guid? playerId = null);

    Task<Result> AddGuestToMatchAsync(Guid groupId, Guid matchId, AddGuestToMatchDto dto, CancellationToken ct);

    Task<Result<IReadOnlyList<PlayerRecentMatchDto>>> GetPlayerRecentMatchesAsync(
        Guid groupId,
        Guid playerId,
        int take,
        CancellationToken ct);

    Task<Result<IReadOnlyList<PlayerRecentMatchDto>>> GetPlayerHistoryAsync(
        Guid groupId,
        Guid playerId,
        int? year,
        CancellationToken ct);

    Task<Result<List<ReplayClipDto>>> GetReplaysAsync(Guid groupId, Guid matchId, Guid? userId, CancellationToken ct);
    Task<(bool IsLiked, int LikeCount)> ToggleLikeAsync(Guid clipId, Guid userId, CancellationToken ct);
    Task<bool> ToggleFavoriteAsync(Guid clipId, Guid userId, CancellationToken ct);
    Task<Result<List<LikedReplayClipDto>>> GetLikedReplaysAsync(Guid groupId, Guid? userId, CancellationToken ct);
    Task<Result<List<LikedReplayClipDto>>> GetAllGroupReplaysAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result<List<LikedReplayClipDto>>> GetMyLikesAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result<List<LikedReplayClipDto>>> GetMyFavoritesAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result<List<ClipLikerDto>>>        GetClipLikersAsync(Guid clipId, CancellationToken ct);
    Task DeleteReplayAsync(Guid groupId, Guid clipId, CancellationToken ct);
    Task<Result<ReplayClipDto>> UploadReplayAsync(Guid groupId, Guid matchId, Guid userId, Stream content, string contentType, string fileName, string eventType, CancellationToken ct);
}
