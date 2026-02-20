using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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

    public async Task<PlayerDto> CreateAsync(CreatePlayerDto request, CancellationToken cancellationToken)
    {
        try
        {
            var groupExists = await _context.Groups
                .AnyAsync(x => x.Id == request.GroupId, cancellationToken);

            if (!groupExists) throw new ApplicationException("Group does not exist.");

            var userExists = await _context.Users
                .AnyAsync(x => x.Id == request.UserId, cancellationToken);

            if (!userExists) throw new ApplicationException("User does not exist.");

            var alreadyExists = await _context.Players
                .AnyAsync(p => p.GroupId == request.GroupId && p.UserId == request.UserId, cancellationToken);

            if (alreadyExists) throw new InvalidOperationException("Player already exists in the group.");

            var player = new PlayerEntity(
                request.Name,  
                request.UserId,
                request.GroupId,
                request.SkillPoints,
                request.IsGoalkeeper,
                request.Status);

            _context.Players.Add(player);           
            await _context.SaveChangesAsync(cancellationToken);

            return new PlayerDto(
                player.Id,
                player.Name,
                player.UserId,
                player.SkillPoints,
                player.IsGoalkeeper,
                player.Status
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating player");
            throw;
        }
    }

    public async Task<PlayerDto> UpdateAsync(Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null) throw new ApplicationException("PlayerEntity not found.");

            player.Rename(request.Name);
            player.SetSkillPoints(request.SkillPoints);
            player.SetGoalkeeper(request.IsGoalkeeper);

            if (request.Status == Status.Inactive && player.Status != Status.Inactive)
                player.Inactivate();
            else if (request.Status == Status.Active && player.Status != Status.Active)
                player.Reactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);

            return new PlayerDto(
                player.Id,
                player.Name,
                player.UserId,
                player.SkillPoints,
                player.IsGoalkeeper,
                player.Status
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player");
            throw;
        }
    }

    public async Task DeleteAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null) throw new ApplicationException("PlayerEntity not found.");

            _repository.Remove(player);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting player");
            throw;
        }
    }

    public async Task<PlayerDto> GetByIdAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdAsync(playerId, cancellationToken);
            if (player == null) throw new ApplicationException("PlayerEntity not found.");

            return new PlayerDto(
                player.Id,
                player.Name,
                player.UserId,
                player.SkillPoints,
                player.IsGoalkeeper,
                player.Status
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player by id");
            throw;
        }
    }

    public async Task InactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null) throw new ApplicationException("PlayerEntity not found.");

            player.Inactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to inactivate player.");
            throw;
        }
    }

    public async Task ReactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null) throw new ApplicationException("PlayerEntity not found.");

            player.Reactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to reactivate player.");
            throw;
        }
    }
}
