using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Dtos.Players;

namespace BratnavaFC.Application.Abstractions;

public interface IGroupService
{
    Task<Guid> CreateAsync(CreateGroupDto request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid groupId, UpdateGroupDto request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid groupId   , CancellationToken cancellationToken);
    Task<GroupDto> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<List<GroupDto>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken);
    Task ActivateAsync(Guid groupId, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Guid> AddPlayerAsync(Guid groupId, CreatePlayerDto request, CancellationToken cancellationToken);
    Task RemovePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken);
    Task DeactivatePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken);
    Task ActivatePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken);
    Task UpdatePlayerAsync(Guid groupId, Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken);
    Task<PlayerDto> GetPlayerByIdAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken);
    Task<List<PlayerDto>> GetPlayersByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);
}
