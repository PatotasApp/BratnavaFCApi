using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
            var adminsExist = await _context.Users.AnyAsync(u => request.UserAdminIds.Contains(u.Id), cancellationToken);
            if (!adminsExist)
                throw new ApplicationException("User admin does not exists.");

            var group = new GroupEntity(request.Name, request.ScheduleMatchDate);
            group.SetAdmins(request.UserAdminIds);

            _repository.Add(group);
            await _repository.SaveChangesAsync(cancellationToken);

            return group.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to create group.");
            throw;
        }
    }

    public async Task UpdateAsync(Guid groupId, UpdateGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                throw new ApplicationException("Group not found.");

            group.Rename(request.Name);
            group.Reschedule(request.ScheduleMatchDate);

            if (request.Status == Status.Inactive && group.Status != Status.Inactive)
                group.Inactivate();
            else if (request.Status == Status.Active && group.Status != Status.Active)
                group.Reactivate();

            _repository.Update(group);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to update group.");
            throw;
        }
    }

    public async Task DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                throw new ApplicationException("Group not found.");

            _repository.Remove(group);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to delete group.");
            throw;
        }
    }

    public async Task InactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                throw new ApplicationException("Group not found.");

            group.Inactivate();

            _repository.Update(group);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to inactivate group.");
            throw;
        }
    }

    public async Task ReactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                throw new ApplicationException("Group not found.");

            group.Reactivate();

            _repository.Update(group);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to reactivate group.");
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
                throw new ApplicationException("Group not found.");

            var players = group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status)).ToList();

            return new GroupDto(
                group.Id,
                group.Name,
                group.ScheduleMatchDate,
                group.Admins.Select(x => x.UserId).ToArray(),
                group.Status,
                players
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to get group by id.");
            throw;
        }
    }

    public Task<List<GroupDto>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        return _context.GroupAdmins
            .Include(x => x.Group)
            .ThenInclude(x => x.Players)
            .Where(x => x.UserId == adminId)
            .Select(g => new GroupDto(
                g.Group.Id,
                g.Group.Name,
                g.Group.ScheduleMatchDate,
                g.Group.Admins.Select(x => x.UserId).ToArray(),
                g.Group.Status,
                g.Group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status)).ToList()
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAdminToGroupAsync(Guid groupId, AddAdminToGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
            if (!userExists)
                throw new ApplicationException("User admin does not exists.");

            var group = await _context.Groups
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                throw new ApplicationException("Group not found.");

            var alreadyAdmin = group.Admins.Any(a => a.UserId == request.UserId);
            if (alreadyAdmin)
                return;

            var newAdmins = group.Admins
                .Select(a => a.UserId)
                .Append(request.UserId)
                .Distinct()
                .ToArray();

            group.SetAdmins(newAdmins);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to add admin to group. GroupId={GroupId} UserId={UserId}", groupId, request?.UserId);
            throw;
        }
    }
}
