using BratnavaFC.Domain.Dtos.Players;

namespace BratnavaFC.Application.Abstractions;

public interface IPlayerService
{
    Task<PlayerDto> CreateAsync(CreatePlayerDto request, CancellationToken cancellationToken);
    Task<PlayerDto> UpdateAsync(Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid playerId, CancellationToken cancellationToken);
    Task<PlayerDto> GetByIdAsync(Guid playerId, CancellationToken cancellationToken);
    Task InactivateAsync(Guid playerId, CancellationToken cancellationToken);
    Task ReactivateAsync(Guid playerId, CancellationToken cancellationToken);
}
