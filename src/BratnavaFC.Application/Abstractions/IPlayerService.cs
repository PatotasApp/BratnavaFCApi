using System;
using BratnavaFC.Domain.Dtos.Players;

namespace BratnavaFC.Application.Abstractions;

public interface IPlayerService
{
    Task CreateAsync(PlayerContracts.CreatePlayerRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(PlayerContracts.UpdatePlayerRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(PlayerContracts.DeletePlayerRequest request, CancellationToken cancellationToken);
    Task<PlayerContracts.GetResponse> GetByIdAsync(Guid playerId, CancellationToken cancellationToken);
}
