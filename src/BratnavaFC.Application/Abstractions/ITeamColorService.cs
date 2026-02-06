using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface ITeamColorService
{
    Task<IReadOnlyList<TeamColorDto>> GetAllAsync(Guid groupId, bool includeInactive, CancellationToken ct);
    Task<TeamColorDto> GetByIdAsync(Guid groupId, Guid colorId, CancellationToken ct);

    Task<TeamColorDto> CreateAsync(CreateTeamColorDto dto, CancellationToken ct);
    Task<TeamColorDto> UpdateAsync(Guid groupId, Guid colorId, UpdateTeamColorDto dto, CancellationToken ct);

    Task InactivateAsync(Guid groupId, Guid colorId, CancellationToken ct);
    Task ActivateAsync(Guid groupId, Guid colorId, CancellationToken ct);
}
