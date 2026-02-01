using System;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public class PlayerService : IPlayerService
{
    private readonly IRepositoryBase<PlayerEntity> _repository;
    private readonly ILogger<PlayerService> _logger;
    private readonly AppDbContext _context;

    public PlayerService(IRepositoryBase<PlayerEntity> repository, ILogger<PlayerService> logger, AppDbContext context)
    {
        _repository = repository;
        _logger = logger;
        _context = context;
    }

    public async Task CreateAsync(PlayerContracts.CreatePlayerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var player = new PlayerEntity
            {
                Name = request.Name,
                UserId = request.UserId,
                GroupId = request.GroupId,
                MainPosition = request.MainPosition,
                Positions = request.Positions,
                SkillPoints = request.SkillPoints,
                Status = request.Status
            };

            _repository.Add(player);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error creating player");
            throw;
        }
    }

    public async Task DeleteAsync(PlayerContracts.DeletePlayerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdAsync(request.PlayerId, cancellationToken);
            
            if (player == null)
            {
                throw new ApplicationException("Player not found.");
            }

            _repository.Remove(player);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error deleting player");
            throw;
        }
    }

    public async Task<PlayerContracts.GetResponse> GetByIdAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdAsync(playerId, cancellationToken);
            if (player == null)
            {
                throw new ApplicationException("Player not found.");
            }

            return new PlayerContracts.GetResponse(
                player.Id,
                player.Name,
                player.UserId,
                player.GroupId,
                player.MainPosition,
                player.Positions,
                player.SkillPoints,
                player.Status
            );
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error getting player by id");
            throw;
        }
    }

    public async Task UpdateAsync(PlayerContracts.UpdatePlayerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (player == null)
            {
                throw new ApplicationException("Player not found.");
            }

            player.Name = request.Name;
            player.MainPosition = request.MainPosition;
            player.Positions = request.Positions;
            player.SkillPoints = request.SkillPoints;
            player.Status = request.Status;

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error updating player");
            throw;
        }
    }
}
