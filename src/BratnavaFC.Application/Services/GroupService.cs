using Microsoft.EntityFrameworkCore;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using System.Linq;

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

    public async Task<Guid> CreateAsync(GroupContracts.CreateGroupRequest request, CancellationToken cancellationToken)
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

    public async Task DeleteAsync(GroupContracts.DeleteGroupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdAsync(request.GroupId, cancellationToken);
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

    public async Task UpdateAsync(Guid groupId, GroupContracts.UpdateGroupRequest request, CancellationToken cancellationToken)
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
            group.Status = request.Status;

            _repository.Update(group);
            _ = await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to update group.");
            throw;
        }
    }

    public async Task<GroupContracts.GetResponse> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
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

            var players = group.Players!.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name)).ToList();

            return new GroupContracts.GetResponse(
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

    public Task<List<GroupContracts.GetResponse>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        return _context.GroupAdmins.Include(x => x.Group).ThenInclude(x => x.Players).Where(x => x.UserId == adminId)
                                    .Select(g => new GroupContracts.GetResponse(g.Group.Id, g.Group.Name, g.Group.ScheduleMatchDate, g.Group.Admins.Select(x => x.UserId).ToArray(), g.Group.Status, g.Group.Players!.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name)).ToList()))
                                    .ToListAsync(cancellationToken);
    }
}
