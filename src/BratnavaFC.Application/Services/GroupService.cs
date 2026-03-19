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

            var group = new GroupEntity(request.Name, request.ScheduleMatchDate, request.CreatedByUserId);
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
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var group = await _context.Groups
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group == null)
                throw new ApplicationException("Group not found.");

            // 1. Partidas → cascateia MatchPlayers, Votes e Goals (MatchId = Cascade)
            var matches = await _context.Matches
                .Where(m => m.GroupId == groupId)
                .ToListAsync(cancellationToken);
            _context.Matches.RemoveRange(matches);
            await _context.SaveChangesAsync(cancellationToken);

            // 2. Cores do time (FK GroupId = Restrict, precisa remoção explícita)
            var colors = await _context.TeamColors
                .Where(c => c.GroupId == groupId)
                .ToListAsync(cancellationToken);
            _context.TeamColors.RemoveRange(colors);
            await _context.SaveChangesAsync(cancellationToken);

            // 3. Configurações do grupo (FK GroupId = Restrict)
            var settings = await _context.GroupSettings
                .Where(s => s.GroupId == groupId)
                .FirstOrDefaultAsync(cancellationToken);
            if (settings != null)
            {
                _context.GroupSettings.Remove(settings);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // 4. Jogadores (MatchPlayers já removidos no passo 1)
            var players = await _context.Players
                .Where(p => p.GroupId == groupId)
                .ToListAsync(cancellationToken);
            _context.Players.RemoveRange(players);
            await _context.SaveChangesAsync(cancellationToken);

            // 5. Grupo (GroupAdmins e GroupInvites cascateiam automaticamente)
            _context.Groups.Remove(group);
            await _context.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to delete group with cascade. GroupId={GroupId}", groupId);
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
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group == null)
                throw new ApplicationException("Group not found.");

            var allPlayers = await _context.Players
                .Include(p => p.User)
                .Where(p => p.GroupId == groupId)
                .ToListAsync(cancellationToken);

            var players = allPlayers.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.User?.UserName, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating)).ToList();

            return new GroupDto(
                group.Id,
                group.Name,
                group.ScheduleMatchDate,
                group.Admins.Select(x => x.UserId).ToArray(),
                group.Status,
                players,
                group.CreatedByUserId
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
                g.Group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, null, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating)).ToList(),
                g.Group.CreatedByUserId
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<GroupDto>> GetAllGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await _context.Groups
            .Include(g => g.Players)
                .ThenInclude(p => p.User)
            .Include(g => g.Admins)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

        return groups.Select(g => new GroupDto(
            g.Id,
            g.Name,
            g.ScheduleMatchDate,
            g.Admins.Select(a => a.UserId).ToArray(),
            g.Status,
            g.Players.Select(p => new Domain.Dtos.Players.PlayerDto(
                p.Id, p.Name, p.UserId, p.User?.UserName,
                p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating)).ToList(),
            g.CreatedByUserId
        )).ToList();
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

            AddAdminToGroupInternal(group, request.UserId);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to add admin to group. GroupId={GroupId} UserId={UserId}", groupId, request?.UserId);
            throw;
        }
    }

    /// <summary>
    /// Adiciona um usuário como admin de um grupo já carregado (sem load/save).
    /// Idempotente: se já for admin, não faz nada.
    /// </summary>
    private static void AddAdminToGroupInternal(GroupEntity group, Guid userId)
    {
        if (group.Admins.Any(a => a.UserId == userId))
            return;

        var newAdmins = group.Admins
            .Select(a => a.UserId)
            .Append(userId)
            .Distinct()
            .ToArray();

        group.SetAdmins(newAdmins);
    }

    public async Task RemoveAdminAsync(Guid groupId, Guid targetUserId, Guid requestingUserId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                throw new ApplicationException("Group not found.");

            var requestingIsAdmin = group.Admins.Any(a => a.UserId == requestingUserId);
            if (!requestingIsAdmin)
                throw new UnauthorizedAccessException("Requesting user is not an admin of this group.");

            group.RemoveAdmin(targetUserId);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to remove admin from group. GroupId={GroupId} TargetUserId={TargetUserId}", groupId, targetUserId);
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

            // Já é membro ativo (não-guest)?
            var alreadyMember = await _context.Players
                .AnyAsync(p => p.GroupId == groupId && p.UserId == request.TargetUserId && !p.IsGuest, cancellationToken);
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

            PlayerEntity thePlayer;

            if (invite.GuestPlayerId.HasValue)
            {
                // Vincular ao guest player existente
                var player = await _context.Players
                    .FirstOrDefaultAsync(p => p.Id == invite.GuestPlayerId.Value, cancellationToken);

                if (player == null) throw new ApplicationException("Guest player not found.");
                player.SetUser(userId);
                player.SetIsGuest(false);
                player.SetJoinedAt(DateTime.UtcNow);
                _context.Players.Update(player);
                thePlayer = player;
            }
            else
            {
                // Verificar se já existe um player guest do mesmo usuário neste grupo (ex-mensalista que virou convidado)
                var existingGuestPlayer = await _context.Players
                    .FirstOrDefaultAsync(p => p.GroupId == invite.GroupId && p.UserId == userId && p.IsGuest, cancellationToken);

                if (existingGuestPlayer != null)
                {
                    existingGuestPlayer.SetIsGuest(false);
                    existingGuestPlayer.SetJoinedAt(DateTime.UtcNow);
                    _context.Players.Update(existingGuestPlayer);
                    thePlayer = existingGuestPlayer;
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
                    thePlayer = newPlayer;
                }
            }

            // Se houver partida em Acceptation no grupo, incluir o novo jogador
            var acceptationMatch = await _context.Matches
                .Include(m => m.Players)
                .FirstOrDefaultAsync(m => m.GroupId == invite.GroupId && m.Status == MatchStatus.Acceptation, cancellationToken);

            if (acceptationMatch != null && !acceptationMatch.Players.Any(mp => mp.PlayerId == thePlayer.Id))
            {
                var mp = new MatchPlayerEntity(thePlayer.Id);
                mp.AssignToMatch(acceptationMatch);   // também chama AssignGroup internamente
                _context.MatchPlayers.Add(mp);
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

    public async Task CreatorLeaveGroupAsync(Guid groupId, Guid requestingUserId, CreatorLeaveGroupDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                throw new ApplicationException("Group not found.");

            if (group.CreatedByUserId != requestingUserId)
                throw new UnauthorizedAccessException("Only the group creator can use this operation.");

            // Opção 1: Deletar o grupo
            if (dto.DeleteGroup)
            {
                await DeleteAsync(groupId, cancellationToken);
                return;
            }

            // Localizar o player do criador neste grupo
            var creatorPlayer = await _context.Players
                .FirstOrDefaultAsync(p => p.GroupId == groupId && p.UserId == requestingUserId, cancellationToken);

            // Opção 2: Transferir para admin existente
            if (dto.TransferToUserId.HasValue)
            {
                var isExistingAdmin = group.Admins.Any(a => a.UserId == dto.TransferToUserId.Value);
                if (!isExistingAdmin)
                    throw new InvalidOperationException("TransferToUserId must be an existing admin.");

                group.TransferCreator(dto.TransferToUserId.Value);

                // Remove o criador original dos admins (agora que CreatedByUserId foi alterado)
                var oldCreatorAdmin = group.Admins.FirstOrDefault(a => a.UserId == requestingUserId);
                if (oldCreatorAdmin != null)
                    group.RemoveAdmin(requestingUserId);

                if (creatorPlayer != null)
                    creatorPlayer.SetIsGuest(true);

                _context.Groups.Update(group);
                if (creatorPlayer != null) _context.Players.Update(creatorPlayer);
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            // Opção 3: Promover jogador (não admin) e transferir
            if (dto.PromoteAndTransferUserId.HasValue)
            {
                var targetUserId = dto.PromoteAndTransferUserId.Value;

                var userExists = await _context.Users.AnyAsync(u => u.Id == targetUserId, cancellationToken);
                if (!userExists)
                    throw new ApplicationException("User to promote not found.");

                // Adicionar como admin (reutiliza lógica de AddAdminToGroupAsync)
                AddAdminToGroupInternal(group, targetUserId);

                // Transferir liderança
                group.TransferCreator(targetUserId);

                // Remover criador original dos admins
                group.RemoveAdmin(requestingUserId);

                if (creatorPlayer != null)
                    creatorPlayer.SetIsGuest(true);

                _context.Groups.Update(group);
                if (creatorPlayer != null) _context.Players.Update(creatorPlayer);
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            throw new InvalidOperationException("Invalid leave operation: provide TransferToUserId, PromoteAndTransferUserId, or set DeleteGroup = true.");
        }
        catch (Exception ex) when (ex is not (ApplicationException or UnauthorizedAccessException or InvalidOperationException))
        {
            _logger.LogError(ex, "Error in CreatorLeaveGroupAsync. GroupId={GroupId}", groupId);
            throw;
        }
    }
}
