using Microsoft.EntityFrameworkCore;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using System.Linq;
using BratnavaFC.Domain.Dtos.Players;

namespace BratnavaFC.Application.Services;

public class GroupService : IGroupService
{
    private readonly AppDbContext _context;
    private readonly ILogger<GroupService> _logger;
    private readonly IRepositoryBase<GroupEntity> _repository;

    public GroupService(AppDbContext context, ILogger<GroupService> logger, IRepositoryBase<GroupEntity> repository)
    {
        _context = context;
        _logger = logger;
        _repository = repository;
    }

    public async Task<Guid> CreateAsync(CreateGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var adminExists = await _context.Users
                 .AnyAsync(u => request.UserAdminIds.Contains(u.Id), cancellationToken);

            if (!adminExists)
            {
                throw new ApplicationException("User admin does not exists.");
            }

            GroupEntity newGroup = new()
            {
                Name = request.Name,
                ScheduleMatchDate = request.ScheduleMatchDate
            };

            newGroup.Admins = request.UserAdminIds.Select(adminId => new GroupAdminEntity()
            {
                GroupId = newGroup.Id,
                UserId = adminId
            }).ToList();

            _repository.Add(newGroup);
            await _repository.SaveChangesAsync(cancellationToken);

            return newGroup.Id;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to create group.");
            throw;
        }
    }

    public async Task DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdAsync(groupId, cancellationToken);
            if (group == null)
            {
                throw new ApplicationException("Group not found.");
            }

            _repository.Remove(group);
            _ = await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to delete group.");
            throw;
        }
    }

    public async Task UpdateAsync(Guid groupId, UpdateGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdAsync(groupId, cancellationToken);

            if (group == null)
            {
                throw new ApplicationException("Group not found.");
            }

            group.Name = request.Name;
            group.ScheduleMatchDate = request.ScheduleMatchDate;

            _repository.Update(group);
            _ = await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to update group.");
            throw;
        }
    }

    public async Task<GroupDto> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Players)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group == null)
            {
                throw new ApplicationException("Group not found.");
            }

            var players = group.Players!.Select(p => new PlayerDto(p.Id, p.Name, p.UserId, p.SkillPoints, p.Status)).ToList();

            return new GroupDto(
                group.Id,
                group.Name,
                group.ScheduleMatchDate,
                group.Admins.Select(x => x.UserId).ToArray(),
                group.Status,
                players
            );
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to get group by id.");
            throw;
        }
    }

    public Task<List<GroupDto>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        return _context.GroupAdmins.Include(x => x.Group).ThenInclude(x => x.Players).Where(x => x.UserId == adminId)
                                    .Select(g => new GroupDto(g.Group.Id, g.Group.Name, g.Group.ScheduleMatchDate, g.Group.Admins.Select(x => x.UserId).ToArray(), g.Group.Status, g.Group.Players!.Select(p => new PlayerDto(p.Id, p.Name, p.UserId, p.SkillPoints, p.Status)).ToList()))
                                    .ToListAsync(cancellationToken);
    }

    public async Task<Guid> AddPlayerAsync(Guid groupId, CreatePlayerDto request, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);

            if (group == null)
            {
                throw new ApplicationException("Group not found.");
            }

            if (group.Players!.Any(x => x.UserId == request.UserId))
            {
                throw new ApplicationException("Player already exists in the group.");
            }

            var player = new PlayerEntity
            {
                Name = request.Name,
                UserId = request.UserId,
                SkillPoints = request.SkillPoints,
                Status = request.Status
            };

            group.AddPlayer(player);

            await _context.SaveChangesAsync(cancellationToken);

            return player.Id;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to add player to group.");
            throw;
        }
    }

    public async Task RemovePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

            group.RemovePlayer(playerId);

            _context.Players.Remove(new PlayerEntity { Id = playerId });

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {   
            _logger.LogError(ex, "Error trying to remove player from group.");
            throw;
        }
    }

    public async Task DeactivatePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

            group.DeactivatePlayer(playerId);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to deactivate player in group.");
            throw;
        }
    }

    public async Task ActivatePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

            group.ActivatePlayer(playerId);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to activate player in group.");
            throw;
        }
    }

    public async Task UpdatePlayerAsync(Guid groupId, Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

            var player = group.Players!.FirstOrDefault(x => x.Id == playerId);

            if (player == null)
            {
                throw new ApplicationException("Player not found.");
            }

            player.Name = request.Name;
            player.SkillPoints = request.SkillPoints;

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to update player in group.");
            throw;
        }
    }

    public async Task<PlayerDto> GetPlayerByIdAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

        var player = group.Players!.FirstOrDefault(x => x.Id == playerId);

        if (player == null)
        {
            throw new ApplicationException("Player not found.");
        }

        return new PlayerDto(player.Id, player.Name, player.UserId, player.SkillPoints, player.Status);
    }

    public async Task<List<PlayerDto>> GetPlayersByGroupIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups.Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

        return group.Players!.Select(p => new PlayerDto(p.Id, p.Name, p.UserId, p.SkillPoints, p.Status)).ToList();
    }

    Task<List<GroupDto>> IGroupService.GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task ActivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

            group.Activate();

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to activate group.");
            throw;
        }
    }

    public async Task DeactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups.FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken) ?? throw new ApplicationException("Group not found.");

            group.Deactivate();

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to deactivate group.");
            throw;
        }
    }
}
