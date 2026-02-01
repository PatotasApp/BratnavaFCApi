using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface ITeamColorService
{
    Task<IEnumerable<TeamColorEntity>> GetAllAsync(CancellationToken cancellationToken);
    Task<TeamColorEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<TeamColorEntity> CreateAsync(TeamColorEntity color, CancellationToken cancellationToken);
    Task UpdateAsync(TeamColorEntity color, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}