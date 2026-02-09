using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface IGroupSettingsService
{
    Task<GroupSettingsDto> GetAsync(Guid groupId, CancellationToken ct);
    Task<GroupSettingsDto> UpsertAsync(Guid groupId, UpsertGroupSettingsDto dto, CancellationToken ct);
}
