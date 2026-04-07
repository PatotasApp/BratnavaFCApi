using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Absences;

namespace BratnavaFC.Application.Abstractions;

public interface IAbsenceService
{
    Task<Result<List<AbsenceDto>>> GetMineAsync(Guid userId, CancellationToken ct = default);
    Task<Result<AbsenceDto>> CreateAsync(Guid userId, CreateAbsenceDto dto, CancellationToken ct = default);
    Task<Result<AbsenceDto>> UpdateAsync(Guid userId, Guid absenceId, CreateAbsenceDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid userId, Guid absenceId, CancellationToken ct = default);
    Task<Result<List<GroupMemberAbsenceDto>>> GetByGroupAsync(Guid groupId, CancellationToken ct = default);
}
