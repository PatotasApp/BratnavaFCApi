using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Discovery;

namespace BratnavaFC.Application.Abstractions;

public interface IDiscoveryService
{
    Task<Result<PagedResultDto<GroupSearchItemDto>>> SearchGroupsAsync(Guid requesterId, string? query, int page, int pageSize, CancellationToken ct);
    Task<Result<PagedResultDto<PlayerSearchItemDto>>> SearchPlayersAsync(Guid requesterId, string? query, int page, int pageSize, CancellationToken ct);
    Task<Result<GroupProfileDto>> GetGroupProfileAsync(Guid groupId, Guid requesterId, CancellationToken ct);
    Task<Result> UpdateGroupProfileAsync(Guid groupId, UpdateGroupProfileDto dto, CancellationToken ct);
    Task<Result<string>> RegenerateGroupCodeAsync(Guid groupId, CancellationToken ct);
    Task<Result<MatchOpeningDto>> CreateOpeningAsync(Guid groupId, Guid matchId, UpsertMatchOpeningDto dto, CancellationToken ct);
    Task<Result<MatchOpeningDto>> UpdateOpeningAsync(Guid groupId, Guid openingId, UpsertMatchOpeningDto dto, CancellationToken ct);
    Task<Result> DeleteOpeningAsync(Guid groupId, Guid openingId, CancellationToken ct);
    Task<Result<MatchApplicationDto>> ApplyAsync(Guid groupId, Guid userId, CreateMatchApplicationDto dto, CancellationToken ct);
    Task<Result<List<MatchApplicationDto>>> GetGroupApplicationsAsync(Guid groupId, bool includeHistory, CancellationToken ct);
    Task<Result<List<MatchApplicationDto>>> GetMyApplicationsAsync(Guid userId, bool includeHistory, CancellationToken ct);
    Task<Result<List<GuestCandidateDto>>> GetGuestCandidatesAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<Result> ApproveAsync(Guid groupId, Guid applicationId, ApproveMatchApplicationDto dto, CancellationToken ct);
    Task<Result<MatchApplicationDto>> InviteAsync(Guid groupId, Guid adminUserId, CreateAdminMatchInviteDto dto, CancellationToken ct);
    Task<Result> RejectByAdminAsync(Guid groupId, Guid applicationId, RejectMatchApplicationDto dto, CancellationToken ct);
    Task<Result> AcceptByPlayerAsync(Guid applicationId, Guid userId, CancellationToken ct);
    Task<Result> RejectByPlayerAsync(Guid applicationId, Guid userId, CancellationToken ct);
    Task<Result<List<GuestMatchSummaryDto>>> GetGuestMatchesAsync(Guid userId, CancellationToken ct);
    Task<Result<GuestMatchDto>> GetGuestMatchAsync(Guid userId, Guid matchId, CancellationToken ct);
    Task<Result<GuestMatchDto>> SetGuestAttendanceAsync(Guid userId, Guid matchId, bool going, CancellationToken ct);
}
