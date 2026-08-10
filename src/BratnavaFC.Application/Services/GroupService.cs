using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
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
    private readonly IPushService _push;

    public GroupService(
        AppDbContext context,
        ILogger<GroupService> logger,
        IRepositoryBase<GroupEntity> repository,
        IPushService push)
    {
        _context = context;
        _logger = logger;
        _repository = repository;
        _push = push;
    }

    public async Task<Result<Guid>> CreateAsync(CreateGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var creator = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.CreatedByUserId, cancellationToken);

            if (creator == null)
                return Result<Guid>.Fail("Usuário administrador não encontrado.", ResultStatus.NotFound);

            var group = new GroupEntity(request.Name, request.ScheduleMatchDate, request.CreatedByUserId);
            group.SetAdmins(request.UserAdminIds);

            var creatorName = $"{creator.FirstName} {creator.LastName}".Trim();
            var creatorPlayer = new PlayerEntity(creatorName, creator.Id, group.Id, 0, false, false, Status.Active);
            _context.Players.Add(creatorPlayer);

            _repository.Add(group);
            await _repository.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Ok(group.Id, "Grupo criado com sucesso.", ResultStatus.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to create group.");
            throw;
        }
    }

    public async Task<Result> UpdateAsync(Guid groupId, UpdateGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            group.Rename(request.Name);
            group.Reschedule(request.ScheduleMatchDate);

            if (request.Status == Status.Inactive && group.Status != Status.Inactive)
                group.Inactivate();
            else if (request.Status == Status.Active && group.Status != Status.Active)
                group.Reactivate();

            _repository.Update(group);
            await _repository.SaveChangesAsync(cancellationToken);

            return Result.Ok("Grupo atualizado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to update group.");
            throw;
        }
    }

    public async Task<Result> DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var group = await _context.Groups
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group == null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

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

            return Result.Ok("Grupo removido com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to delete group with cascade. GroupId={GroupId}", groupId);
            throw;
        }
    }

    public async Task<Result> InactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            group.Inactivate();

            _repository.Update(group);
            await _repository.SaveChangesAsync(cancellationToken);

            return Result.Ok("Grupo atualizado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to inactivate group.");
            throw;
        }
    }

    public async Task<Result> ReactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _repository.GetByIdIncludingInactiveAsync(groupId, cancellationToken);
            if (group == null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            group.Reactivate();

            _repository.Update(group);
            await _repository.SaveChangesAsync(cancellationToken);

            return Result.Ok("Grupo atualizado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to reactivate group.");
            throw;
        }
    }

    public async Task<Result<GroupDto>> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Admins)
                .Include(g => g.Financeiros)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group == null)
                return Result<GroupDto>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            var allPlayers = await _context.Players
                .Include(p => p.User)
                .Where(p => p.GroupId == groupId)
                .ToListAsync(cancellationToken);

            var players = allPlayers.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.User?.UserName, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating, p.AttackRating, p.DefenseRating, p.OverallRating, PhotoUrl(p.User))).ToList();

            var adminUserIds      = group.Admins.Select(x => x.UserId).ToArray();
            var financeiroUserIds = group.Financeiros.Select(x => x.UserId).ToArray();
            var allRoleIds        = adminUserIds.Concat(financeiroUserIds).Distinct().ToList();

            var roleNames = await _context.Users
                .AsNoTracking()
                .Where(u => allRoleIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

            var dto = new GroupDto(
                group.Id, group.Name, group.ScheduleMatchDate,
                adminUserIds, financeiroUserIds,
                group.Status, players, group.CreatedByUserId)
            {
                AdminNames      = adminUserIds.Select(id => roleNames.GetValueOrDefault(id, "")).ToArray(),
                FinanceiroNames = financeiroUserIds.Select(id => roleNames.GetValueOrDefault(id, "")).ToArray(),
                LogoUrl = LogoUrl(group),
                LogoUpdatedAt = group.LogoUpdatedAt,
            };

            return Result<GroupDto>.Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to get group by id.");
            throw;
        }
    }

    public async Task<Result<List<GroupDto>>> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        var list = await _context.GroupAdmins
            .Include(x => x.Group).ThenInclude(g => g.Players).ThenInclude(p => p.User)
            .Include(x => x.Group).ThenInclude(g => g.Admins)
            .Include(x => x.Group).ThenInclude(g => g.Financeiros)
            .Where(x => x.UserId == adminId)
            .Select(g => new GroupDto(
                g.Group.Id,
                g.Group.Name,
                g.Group.ScheduleMatchDate,
                g.Group.Admins.Select(x => x.UserId).ToArray(),
                g.Group.Financeiros.Select(x => x.UserId).ToArray(),
                g.Group.Status,
                g.Group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.User != null ? p.User.UserName : null, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating, p.AttackRating, p.DefenseRating, p.OverallRating, p.User != null && p.User.ProfilePhotoData != null ? "/api/Users/" + p.UserId + "/photo" : null)).ToList(),
                g.Group.CreatedByUserId
            )
            {
                LogoUrl = g.Group.LogoUpdatedAt.HasValue
                    ? "/api/Groups/" + g.Group.Id + "/logo"
                    : null,
                LogoUpdatedAt = g.Group.LogoUpdatedAt
            })
            .ToListAsync(cancellationToken);

        return Result<List<GroupDto>>.Ok(list);
    }

    public async Task<Result<List<GroupDto>>> GetByFinanceiroIdAsync(Guid financeiroId, CancellationToken cancellationToken)
    {
        var list = await _context.GroupFinanceiros
            .Include(x => x.Group).ThenInclude(g => g.Players).ThenInclude(p => p.User)
            .Include(x => x.Group).ThenInclude(g => g.Admins)
            .Include(x => x.Group).ThenInclude(g => g.Financeiros)
            .Where(x => x.UserId == financeiroId)
            .Select(g => new GroupDto(
                g.Group.Id,
                g.Group.Name,
                g.Group.ScheduleMatchDate,
                g.Group.Admins.Select(x => x.UserId).ToArray(),
                g.Group.Financeiros.Select(x => x.UserId).ToArray(),
                g.Group.Status,
                g.Group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.User != null ? p.User.UserName : null, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating, p.AttackRating, p.DefenseRating, p.OverallRating, p.User != null && p.User.ProfilePhotoData != null ? "/api/Users/" + p.UserId + "/photo" : null)).ToList(),
                g.Group.CreatedByUserId
            )
            {
                LogoUrl = g.Group.LogoUpdatedAt.HasValue
                    ? "/api/Groups/" + g.Group.Id + "/logo"
                    : null,
                LogoUpdatedAt = g.Group.LogoUpdatedAt
            })
            .ToListAsync(cancellationToken);

        return Result<List<GroupDto>>.Ok(list);
    }

    public async Task<Result<PagedResultDto<GroupDto>>> GetAllGroupsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _context.Groups.AsQueryable();
        var total = await query.CountAsync(cancellationToken);

        var groups = await query
            .Include(g => g.Players)
                .ThenInclude(p => p.User)
            .Include(g => g.Admins)
            .Include(g => g.Financeiros)
            .OrderBy(g => g.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var list = groups.Select(g => new GroupDto(
            g.Id,
            g.Name,
            g.ScheduleMatchDate,
            g.Admins.Select(a => a.UserId).ToArray(),
            g.Financeiros.Select(f => f.UserId).ToArray(),
            g.Status,
            g.Players.Select(p => new Domain.Dtos.Players.PlayerDto(
                p.Id, p.Name, p.UserId, p.User?.UserName,
                p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating,
                p.AttackRating, p.DefenseRating, p.OverallRating, PhotoUrl(p.User))).ToList(),
            g.CreatedByUserId
        )
        {
            LogoUrl = LogoUrl(g),
            LogoUpdatedAt = g.LogoUpdatedAt
        }).ToList();

        return Result<PagedResultDto<GroupDto>>.Ok(new PagedResultDto<GroupDto>
        {
            Page = page, PageSize = pageSize, Total = total, Items = list,
        });
    }

    private static string? PhotoUrl(UserEntity? user) =>
        user?.ProfilePhotoData is { Length: > 0 }
            ? $"/api/Users/{user.Id}/photo?v={user.ProfilePhotoUpdatedAt?.ToUnixTimeMilliseconds()}"
            : null;

    private static string? LogoUrl(GroupEntity group) =>
        group.LogoUpdatedAt.HasValue
            ? $"/api/Groups/{group.Id}/logo?v={group.LogoUpdatedAt.Value.ToUnixTimeMilliseconds()}"
            : null;

    public async Task<Result<GroupLogoDto>> SetLogoAsync(
        Guid groupId,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (data.Length == 0 || data.Length > 5 * 1024 * 1024)
            return Result<GroupLogoDto>.Fail("A logo deve ter no máximo 5 MB.", ResultStatus.BadRequest);

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        if (!IsSupportedImage(data, normalizedContentType))
            return Result<GroupLogoDto>.Fail("Envie uma imagem JPEG, PNG ou WebP válida.", ResultStatus.BadRequest);

        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
            return Result<GroupLogoDto>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        group.SetLogo(data, normalizedContentType);
        await _context.SaveChangesAsync(cancellationToken);

        var updatedAt = group.LogoUpdatedAt ?? DateTimeOffset.UtcNow;
        return Result<GroupLogoDto>.Ok(new GroupLogoDto(
            $"/api/Groups/{group.Id}/logo?v={updatedAt.ToUnixTimeMilliseconds()}",
            updatedAt));
    }

    public async Task<Result<(byte[] Data, string ContentType, DateTimeOffset UpdatedAt)>> GetLogoAsync(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var logo = await _context.Groups
            .AsNoTracking()
            .Where(g => g.Id == groupId && g.LogoData != null)
            .Select(g => new
            {
                Data = g.LogoData!,
                ContentType = g.LogoContentType!,
                UpdatedAt = g.LogoUpdatedAt!.Value
            })
            .FirstOrDefaultAsync(cancellationToken);

        return logo is null
            ? Result<(byte[], string, DateTimeOffset)>.Fail("Logo não encontrada.", ResultStatus.NotFound)
            : Result<(byte[], string, DateTimeOffset)>.Ok((logo.Data, logo.ContentType, logo.UpdatedAt));
    }

    public async Task<Result> RemoveLogoAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
            return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        group.RemoveLogo();
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Ok("Logo removida com sucesso.");
    }

    private static bool IsSupportedImage(byte[] data, string contentType) => contentType switch
    {
        "image/jpeg" => data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF,
        "image/png" => data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47,
        "image/webp" => data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50,
        _ => false
    };

    public async Task<Result> AddAdminToGroupAsync(Guid groupId, AddAdminToGroupDto request, CancellationToken cancellationToken)
    {
        try
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
            if (!userExists)
                return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

            var group = await _context.Groups
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            AddAdminToGroupInternal(group, request.UserId);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);

            await NotifyUserPromotedAdminAsync(request.UserId, group.Name, groupId, cancellationToken);

            return Result.Ok("Admin adicionado com sucesso.");
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

    public async Task<Result> RemoveAdminAsync(Guid groupId, Guid targetUserId, Guid requestingUserId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            var requestingIsAdmin = group.Admins.Any(a => a.UserId == requestingUserId);
            if (!requestingIsAdmin)
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);

            group.RemoveAdmin(targetUserId);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Ok("Admin removido com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to remove admin from group. GroupId={GroupId} TargetUserId={TargetUserId}", groupId, targetUserId);
            throw;
        }
    }

    // ── Financeiros ───────────────────────────────────────────────────────────

    public async Task<Result> AddFinanceiroToGroupAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId, cancellationToken);
            if (!userExists)
                return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

            var group = await _context.Groups
                .Include(g => g.Financeiros)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            group.AddFinanceiro(userId);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);

            await NotifyUserPromotedFinanceiroAsync(userId, group.Name, groupId, cancellationToken);

            return Result.Ok("Financeiro adicionado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding financeiro to group. GroupId={GroupId} UserId={UserId}", groupId, userId);
            throw;
        }
    }

    public async Task<Result> RemoveFinanceiroAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Financeiros)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            group.RemoveFinanceiro(userId);

            _context.Groups.Update(group);
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Ok("Financeiro removido com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing financeiro from group. GroupId={GroupId} UserId={UserId}", groupId, userId);
            throw;
        }
    }

    // ── Convites ──────────────────────────────────────────────────────────────

    public async Task<Result<GroupInviteDto>> CreateInviteAsync(Guid groupId, CreateGroupInviteDto request, CancellationToken cancellationToken)
    {
        try
        {
            var groupExists = await _context.Groups.AnyAsync(g => g.Id == groupId, cancellationToken);
            if (!groupExists)
                return Result<GroupInviteDto>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            var userExists = await _context.Users.AnyAsync(u => u.Id == request.TargetUserId, cancellationToken);
            if (!userExists)
                return Result<GroupInviteDto>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

            // Já é membro ativo (não-guest)?
            var alreadyMember = await _context.Players
                .AnyAsync(p => p.GroupId == groupId && p.UserId == request.TargetUserId && !p.IsGuest, cancellationToken);
            if (alreadyMember)
                return Result<GroupInviteDto>.Fail("Usuário já é membro deste grupo.", ResultStatus.BadRequest);

            // Já tem convite pendente?
            var alreadyPending = await _context.GroupInvites
                .AnyAsync(i => i.GroupId == groupId && i.TargetUserId == request.TargetUserId
                            && i.Status == GroupInviteStatus.Pending, cancellationToken);
            if (alreadyPending)
                return Result<GroupInviteDto>.Fail("Já existe um convite pendente para este usuário.", ResultStatus.BadRequest);

            // Validar guest player (se informado)
            string? guestPlayerName = null;
            if (request.GuestPlayerId.HasValue)
            {
                var guest = await _context.Players
                    .FirstOrDefaultAsync(p => p.Id == request.GuestPlayerId.Value && p.GroupId == groupId && p.IsGuest, cancellationToken);
                if (guest == null)
                    return Result<GroupInviteDto>.Fail("Jogador convidado não encontrado neste grupo.", ResultStatus.NotFound);
                guestPlayerName = guest.Name;
            }

            var invite = new GroupInviteEntity(groupId, request.TargetUserId, request.GuestPlayerId);
            _context.GroupInvites.Add(invite);
            await _context.SaveChangesAsync(cancellationToken);

            var group = await _context.Groups.FindAsync([groupId], cancellationToken);

            await NotifyGroupInviteSentAsync(request.TargetUserId, group?.Name ?? "grupo", groupId, cancellationToken);

            var dto = new GroupInviteDto(
                invite.Id,
                invite.GroupId,
                group?.Name ?? "",
                invite.TargetUserId,
                invite.GuestPlayerId,
                guestPlayerName,
                (int)invite.Status,
                invite.CreateDate,
                group is null ? null : LogoUrl(group)
            );

            return Result<GroupInviteDto>.Ok(dto, "Convite criado com sucesso.", ResultStatus.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group invite. GroupId={GroupId}", groupId);
            throw;
        }
    }

    public async Task<Result<List<GroupInviteDto>>> GetMyInvitesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var list = await _context.GroupInvites
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
                i.CreateDate,
                i.Group.LogoUpdatedAt.HasValue ? "/api/Groups/" + i.GroupId + "/logo" : null
            ))
            .ToListAsync(cancellationToken);

        return Result<List<GroupInviteDto>>.Ok(list);
    }

    public async Task<Result<int>> GetMyPendingInviteCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var count = await _context.GroupInvites
            .CountAsync(i => i.TargetUserId == userId && i.Status == GroupInviteStatus.Pending, cancellationToken);

        return Result<int>.Ok(count);
    }

    public async Task<Result<List<GroupPendingInviteAdminDto>>> GetGroupPendingInvitesAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var list = await _context.GroupInvites
            .Include(i => i.TargetUser)
            .Where(i => i.GroupId == groupId && i.Status == GroupInviteStatus.Pending)
            .OrderByDescending(i => i.CreateDate)
            .Select(i => new GroupPendingInviteAdminDto(
                i.Id,
                i.TargetUserId,
                $"{i.TargetUser.FirstName} {i.TargetUser.LastName}".Trim(),
                i.TargetUser.UserName,
                i.CreateDate))
            .ToListAsync(cancellationToken);

        return Result<List<GroupPendingInviteAdminDto>>.Ok(list);
    }

    public async Task<Result> CancelInviteAsync(Guid groupId, Guid inviteId, CancellationToken cancellationToken)
    {
        var invite = await _context.GroupInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.GroupId == groupId && i.Status == GroupInviteStatus.Pending, cancellationToken);

        if (invite == null)
            return Result.Fail("Convite não encontrado ou já não está pendente.", ResultStatus.NotFound);

        _context.GroupInvites.Remove(invite);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Ok("Convite cancelado.");
    }

    public async Task<Result> AcceptInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var invite = await _context.GroupInvites
                .Include(i => i.Group)
                .FirstOrDefaultAsync(i => i.Id == inviteId && i.TargetUserId == userId, cancellationToken);

            if (invite == null)
                return Result.Fail("Convite não encontrado.", ResultStatus.NotFound);

            if (invite.Status != GroupInviteStatus.Pending)
                return Result.Fail("Convite não está pendente.", ResultStatus.BadRequest);

            PlayerEntity thePlayer;

            if (invite.GuestPlayerId.HasValue)
            {
                // Vincular ao guest player existente
                var player = await _context.Players
                    .FirstOrDefaultAsync(p => p.Id == invite.GuestPlayerId.Value, cancellationToken);

                if (player == null)
                    return Result.Fail("Jogador convidado não encontrado.", ResultStatus.NotFound);

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
                    if (user == null)
                        return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

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

                if (thePlayer.UserId != null)
                {
                    var matchDate = DateOnly.FromDateTime(acceptationMatch.PlayedAt);
                    var absence   = await _context.UserAbsences
                        .Where(a => a.UserId == thePlayer.UserId.Value &&
                                    a.StartDate <= matchDate &&
                                    a.EndDate   >= matchDate)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (absence != null)
                        mp.AutoRejectByAbsence(absence.Id);
                }
            }

            invite.Accept();
            _context.GroupInvites.Update(invite);
            await _context.SaveChangesAsync(cancellationToken);

            _ = NotifyAdminsInviteAcceptedAsync(invite.GroupId, thePlayer.Name, invite.Group.Name, cancellationToken);

            return Result.Ok("Convite aceito com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting invite. InviteId={InviteId}", inviteId);
            throw;
        }
    }

    public async Task<Result> RejectInviteAsync(Guid inviteId, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var invite = await _context.GroupInvites
                .FirstOrDefaultAsync(i => i.Id == inviteId && i.TargetUserId == userId, cancellationToken);

            if (invite == null)
                return Result.Fail("Convite não encontrado.", ResultStatus.NotFound);

            if (invite.Status != GroupInviteStatus.Pending)
                return Result.Fail("Convite não está pendente.", ResultStatus.BadRequest);

            invite.Reject();
            _context.GroupInvites.Update(invite);
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Ok("Convite rejeitado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting invite. InviteId={InviteId}", inviteId);
            throw;
        }
    }

    // ── Notificações ──────────────────────────────────────────────────────────

    private Task NotifyUserPromotedAdminAsync(Guid userId, string groupName, Guid groupId, CancellationToken ct) =>
        _push.SendToUserAsync(
            userId,
            title: "Você agora é administrador!",
            body:  $"Você foi promovido a administrador do grupo \"{groupName}\".",
            data:  new Dictionary<string, string> { ["type"] = "promoted_admin", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);

    private Task NotifyUserPromotedFinanceiroAsync(Guid userId, string groupName, Guid groupId, CancellationToken ct) =>
        _push.SendToUserAsync(
            userId,
            title: "Você agora é financeiro!",
            body:  $"Você foi promovido a financeiro do grupo \"{groupName}\".",
            data:  new Dictionary<string, string> { ["type"] = "promoted_financeiro", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);

    private Task NotifyGroupInviteSentAsync(Guid targetUserId, string groupName, Guid groupId, CancellationToken ct) =>
        _push.SendToUserAsync(
            targetUserId,
            title: "Convite para grupo",
            body:  $"Você foi convidado para o grupo \"{groupName}\". Acesse o app para aceitar!",
            data:  new Dictionary<string, string> { ["type"] = "group_invite", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);

    /// <summary>
    /// Notifica admins do grupo quando um convite é aceito.
    /// Fire-and-forget — não bloqueia a resposta do endpoint.
    /// </summary>
    private async Task NotifyAdminsInviteAcceptedAsync(Guid groupId, string playerName, string groupName, CancellationToken ct)
    {
        try
        {
            await _push.SendToGroupAdminsAsync(
                groupId,
                title: "Novo membro!",
                body:  $"{playerName} aceitou o convite e entrou no grupo \"{groupName}\".",
                data:  new Dictionary<string, string> { ["type"] = "invite_accepted", ["groupId"] = groupId.ToString() },
                ct);
        }
        catch { /* notificação não crítica */ }
    }

    public async Task<Result> CreatorLeaveGroupAsync(Guid groupId, Guid requestingUserId, CreatorLeaveGroupDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _context.Groups
                .Include(g => g.Admins)
                .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

            if (group is null)
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            if (group.CreatedByUserId != requestingUserId)
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);

            // Opção 1: Deletar o grupo
            if (dto.DeleteGroup)
            {
                var deleteResult = await DeleteAsync(groupId, cancellationToken);
                return deleteResult.Success
                    ? Result.Ok("Grupo removido com sucesso.")
                    : deleteResult;
            }

            // Localizar o player do criador neste grupo
            var creatorPlayer = await _context.Players
                .FirstOrDefaultAsync(p => p.GroupId == groupId && p.UserId == requestingUserId, cancellationToken);

            // Opção 2: Transferir para admin existente
            if (dto.TransferToUserId.HasValue)
            {
                var isExistingAdmin = group.Admins.Any(a => a.UserId == dto.TransferToUserId.Value);
                if (!isExistingAdmin)
                    return Result.Fail("TransferToUserId deve ser um admin existente.", ResultStatus.BadRequest);

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
                return Result.Ok("Grupo atualizado com sucesso.");
            }

            // Opção 3: Promover jogador (não admin) e transferir
            if (dto.PromoteAndTransferUserId.HasValue)
            {
                var targetUserId = dto.PromoteAndTransferUserId.Value;

                var userExists = await _context.Users.AnyAsync(u => u.Id == targetUserId, cancellationToken);
                if (!userExists)
                    return Result.Fail("Usuário a promover não encontrado.", ResultStatus.NotFound);

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
                return Result.Ok("Grupo atualizado com sucesso.");
            }

            return Result.Fail("Operação inválida: forneça TransferToUserId, PromoteAndTransferUserId, ou defina DeleteGroup = true.", ResultStatus.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreatorLeaveGroupAsync. GroupId={GroupId}", groupId);
            throw;
        }
    }
}
