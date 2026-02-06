using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface ITeamColorService
{
    Task<IEnumerable<TeamColorEntity>> GetAllAsync(CancellationToken ct);
    Task<TeamColorEntity?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<TeamColorEntity> CreateAsync(CreateTeamColorDto dto, CancellationToken ct);
    Task UpdateAsync(Guid id, UpdateTeamColorDto dto, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}