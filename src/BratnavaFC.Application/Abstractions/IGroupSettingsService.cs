using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface IGroupSettingsService
{
    Task<Result<GroupSettingsDto>> GetAsync(Guid groupId, CancellationToken ct);
    Task<Result<GroupSettingsDto>> UpsertAsync(Guid groupId, UpsertGroupSettingsDto dto, CancellationToken ct);
}
