using BratnavaFC.Domain.Dtos.Groups;

namespace BratnavaFC.Application.Abstractions;

public interface IGroupService
{
    Task<Guid> CreateAsync(CreateGroupDto request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid groupId, UpdateGroupDto request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid groupId, CancellationToken cancellationToken);
    Task<GroupDto> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<List<GroupDto>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken);
    Task InactivateAsync(Guid groupId, CancellationToken cancellationToken);
    Task ReactivateAsync(Guid groupId, CancellationToken cancellationToken);
    Task AddAdminToGroupAsync(Guid groupId, AddAdminToGroupDto request, CancellationToken cancellationToken);

    // ── Convites ──────────────────────────────────────────────────────────────
    Task<GroupInviteDto> CreateInviteAsync(Guid groupId, CreateGroupInviteDto request, CancellationToken cancellationToken);
    Task<List<GroupInviteDto>> GetMyInvitesAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> GetMyPendingInviteCountAsync(Guid userId, CancellationToken cancellationToken);
    Task AcceptInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken);
    Task RejectInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken);
}
