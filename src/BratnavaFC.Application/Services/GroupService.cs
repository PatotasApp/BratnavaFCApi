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

    // ── Convites ──────────────────────────────────────────────────────────────

    public async Task<GroupInviteDto> CreateInviteAsync(Guid groupId, CreateGroupInviteDto request, CancellationToken cancellationToken)
    {
        try
        {
            var groupExists = await _context.Groups.AnyAsync(g => g.Id == groupId, cancellationToken);
            if (!groupExists) throw new ApplicationException("Group not found.");

            var userExists = await _context.Users.AnyAsync(u => u.Id == request.TargetUserId, cancellationToken);
            if (!userExists) throw new ApplicationException("Target user not found.");

            // Já é membro?
            var alreadyMember = await _context.Players
                .AnyAsync(p => p.GroupId == groupId && p.UserId == request.TargetUserId, cancellationToken);
            if (alreadyMember) throw new InvalidOperationException("User is already a member of this group.");

            // Já tem convite pendente?
            var alreadyPending = await _context.GroupInvites
                .AnyAsync(i => i.GroupId == groupId && i.TargetUserId == request.TargetUserId
                            && i.Status == GroupInviteStatus.Pending, cancellationToken);
            if (alreadyPending) throw new InvalidOperationException("There is already a pending invite for this user.");

            // Validar guest player (se informado)
            string? guestPlayerName = null;
            if (request.GuestPlayerId.HasValue)
            {
                var guest = await _context.Players
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == request.GuestPlayerId.Value && p.GroupId == groupId && p.IsGuest, cancellationToken);
                if (guest == null)
                    throw new ApplicationException("Guest player not found in this group.");
                guestPlayerName = guest.Name;
            }

            var invite = new GroupInviteEntity(groupId, request.TargetUserId, request.GuestPlayerId);
            _context.GroupInvites.Add(invite);
            await _context.SaveChangesAsync(cancellationToken);

            var group = await _context.Groups.FindAsync([groupId], cancellationToken);

            return new GroupInviteDto(
                invite.Id,
                invite.GroupId,
                group?.Name ?? "",
                invite.TargetUserId,
                invite.GuestPlayerId,
                guestPlayerName,
                (int)invite.Status,
                invite.CreateDate
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group invite. GroupId={GroupId}", groupId);
            throw;
        }
    }

    public async Task<List<GroupInviteDto>> GetMyInvitesAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.GroupInvites
            .Include(i => i.Group)
            .Include(i => i.GuestPlayer)
            .Where(i => i.TargetUserId == userId && i.Status == GroupInviteStatus.Pending)
            .OrderByDescending(i => i.CreateDate)
            .Select(i => new GroupInviteDto(
                i.Id,
                i.GroupId,
                i.Group.Name,
                i.TargetUserId,
                i.GuestPlayerId,
                i.GuestPlayer != null ? i.GuestPlayer.Name : null,
                (int)i.Status,
                i.CreateDate
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMyPendingInviteCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.GroupInvites
            .CountAsync(i => i.TargetUserId == userId && i.Status == GroupInviteStatus.Pending, cancellationToken);
    }

    public async Task AcceptInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var invite = await _context.GroupInvites
                .Include(i => i.Group)
                .FirstOrDefaultAsync(i => i.Id == inviteId && i.TargetUserId == userId, cancellationToken);

            if (invite == null) throw new ApplicationException("Invite not found.");
            if (invite.Status != GroupInviteStatus.Pending) throw new InvalidOperationException("Invite is not pending.");

            if (invite.GuestPlayerId.HasValue)
            {
                // Vincular ao guest player existente
                var player = await _context.Players
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == invite.GuestPlayerId.Value, cancellationToken);

                if (player == null) throw new ApplicationException("Guest player not found.");
                player.SetUser(userId);
                player.SetIsGuest(false);
                _context.Players.Update(player);
            }
            else
            {
                // Criar novo player para o usuário
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (user == null) throw new ApplicationException("User not found.");

                var name = $"{user.FirstName} {user.LastName}".Trim();
                var newPlayer = new PlayerEntity(name, userId, invite.GroupId, 0, false, false, Status.Active);
                _context.Players.Add(newPlayer);
            }

            invite.Accept();
            _context.GroupInvites.Update(invite);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting invite. InviteId={InviteId}", inviteId);
            throw;
        }
    }

    public async Task RejectInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var invite = await _context.GroupInvites
                .FirstOrDefaultAsync(i => i.Id == inviteId && i.TargetUserId == userId, cancellationToken);

            if (invite == null) throw new ApplicationException("Invite not found.");
            if (invite.Status != GroupInviteStatus.Pending) throw new InvalidOperationException("Invite is not pending.");

            invite.Reject();
            _context.GroupInvites.Update(invite);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting invite. InviteId={InviteId}", inviteId);
            throw;
        }
    }
}
