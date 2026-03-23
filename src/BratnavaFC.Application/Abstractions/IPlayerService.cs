using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Dtos;
namespace BratnavaFC.Application.Abstractions;

public interface IPlayerService
{
    Task<Result<PlayerDto>> CreateAsync(CreatePlayerDto request, CancellationToken cancellationToken);
    Task<Result<PlayerDto>> UpdateAsync(Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid playerId, CancellationToken cancellationToken);
    Task<Result<PlayerDto>> GetByIdAsync(Guid playerId, CancellationToken cancellationToken);
    Task<Result> InactivateAsync(Guid playerId, CancellationToken cancellationToken);
    Task<Result> ReactivateAsync(Guid playerId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<MyPlayerDto>>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> LeaveGroupAsync(Guid playerId, Guid requestingUserId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<BirthdayStatusDto>>> GetBirthdayStatusAsync(Guid groupId, CancellationToken cancellationToken);
}
