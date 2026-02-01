using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface ITeamColorService
{
    Task<IEnumerable<TeamColorEntity>> GetAllAsync();
    Task<TeamColorEntity?> GetByIdAsync(Guid id);
    Task<TeamColorEntity> CreateAsync(TeamColorEntity color);
    Task UpdateAsync(TeamColorEntity color);
    Task DeleteAsync(Guid id);
}