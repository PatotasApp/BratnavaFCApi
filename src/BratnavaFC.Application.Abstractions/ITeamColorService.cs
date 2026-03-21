using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface ITeamColorService
{
    Task<Result<IReadOnlyList<TeamColorDto>>> GetAllAsync(Guid groupId, bool activeOnly, CancellationToken ct);
    Task<Result<TeamColorDto>> GetByIdAsync(Guid groupId, Guid colorId, CancellationToken ct);

    Task<Result<TeamColorDto>> CreateAsync(CreateTeamColorDto dto, CancellationToken ct);
    Task<Result<TeamColorDto>> UpdateAsync(Guid groupId, Guid colorId, UpdateTeamColorDto dto, CancellationToken ct);

    Task<Result> InactivateAsync(Guid groupId, Guid colorId, CancellationToken ct);
    Task<Result> ActivateAsync(Guid groupId, Guid colorId, CancellationToken ct);
}
