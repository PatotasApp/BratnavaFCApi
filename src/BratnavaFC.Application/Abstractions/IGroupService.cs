using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Groups;

namespace BratnavaFC.Application.Abstractions;

public interface IGroupService
{
    Task<Result<Guid>> CreateAsync(CreateGroupDto request, CancellationToken cancellationToken);
    Task<Result> UpdateAsync(Guid groupId, UpdateGroupDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Patotas que ficarão sem nenhuma conta quando este usuário sair delas.</summary>
    Task<List<Guid>> FindAbandonedByAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Apaga patotas na ordem que as chaves estrangeiras RESTRICT exigem, sem abrir transação:
    /// quem chama decide o escopo.
    /// </summary>
    Task DeleteManyAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken);
    Task<Result<GroupDto>> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Result<List<GroupDto>>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken);
    Task<Result<PagedResultDto<GroupDto>>> GetAllGroupsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<Result> InactivateAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Result> ReactivateAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Result<GroupLogoDto>> SetLogoAsync(Guid groupId, Stream image, CancellationToken cancellationToken);
    Task<Result> RemoveLogoAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Result> AddAdminToGroupAsync(Guid groupId, AddAdminToGroupDto request, CancellationToken cancellationToken);
    Task<Result> RemoveAdminAsync(Guid groupId, Guid targetUserId, Guid requestingUserId, CancellationToken cancellationToken);

    // ── Financeiros ───────────────────────────────────────────────────────────
    Task<Result> AddFinanceiroToGroupAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);
    Task<Result> RemoveFinanceiroAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);
    Task<Result<List<GroupDto>>> GetByFinanceiroIdAsync(Guid financeiroId, CancellationToken cancellationToken);

    // ── Convites ──────────────────────────────────────────────────────────────
    Task<Result<GroupInviteDto>> CreateInviteAsync(Guid groupId, CreateGroupInviteDto request, CancellationToken cancellationToken);
    Task<Result<List<GroupInviteDto>>> GetMyInvitesAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<int>> GetMyPendingInviteCountAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<List<GroupPendingInviteAdminDto>>> GetGroupPendingInvitesAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Result> CancelInviteAsync(Guid groupId, Guid inviteId, CancellationToken cancellationToken);
    Task<Result> AcceptInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken);
    Task<Result> RejectInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken);
    Task<Result> CreatorLeaveGroupAsync(Guid groupId, Guid requestingUserId, CreatorLeaveGroupDto dto, CancellationToken cancellationToken);
}
