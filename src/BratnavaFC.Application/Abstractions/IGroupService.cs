using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Dtos.Groups;

namespace BratnavaFC.Application.Abstractions;

public interface IGroupService
{
    Task<Guid> CreateAsync(GroupContracts.CreateGroupRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid groupId, GroupContracts.UpdateGroupRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(GroupContracts.DeleteGroupRequest request, CancellationToken cancellationToken);
    Task<GroupContracts.GetResponse> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<List<GroupContracts.GetResponse>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken);
}
