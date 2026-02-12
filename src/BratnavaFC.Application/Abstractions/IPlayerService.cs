using BratnavaFC.Domain.Dtos.Players;

public interface IPlayerService
{
    Task<Guid> CreateAsync(PlayerContracts.CreatePlayerRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid playerId, PlayerContracts.UpdatePlayerRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(PlayerContracts.DeletePlayerRequest request, CancellationToken cancellationToken);
    Task<PlayerContracts.GetResponse> GetByIdAsync(Guid playerId, CancellationToken cancellationToken);

    Task InactivateAsync(Guid playerId, CancellationToken cancellationToken);
    Task ReactivateAsync(Guid playerId, CancellationToken cancellationToken);
}
