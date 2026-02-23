using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Dtos;
namespace BratnavaFC.Application.Abstractions;

public interface IPlayerService
{
    Task<PlayerDto> CreateAsync(CreatePlayerDto request, CancellationToken cancellationToken);
    Task<PlayerDto> UpdateAsync(Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid playerId, CancellationToken cancellationToken);
    Task<PlayerDto> GetByIdAsync(Guid playerId, CancellationToken cancellationToken);
    Task InactivateAsync(Guid playerId, CancellationToken cancellationToken);
    Task ReactivateAsync(Guid playerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MyPlayerDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
