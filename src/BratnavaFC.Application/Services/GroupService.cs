using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
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
    private readonly IImageStorageService _images;

    public GroupService(
        AppDbContext context,
        ILogger<GroupService> logger,
        IRepositoryBase<GroupEntity> repository,
        IPushService push,
        IImageStorageService images)
    {
        _context = context;
        _logger = logger;
        _repository = repository;
        _push = push;
        _images = images;
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
            if (!await _context.Groups.AnyAsync(g => g.Id == groupId, cancellationToken))
                return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            await DeleteManyAsync([groupId], cancellationToken);

            await tx.CommitAsync(cancellationToken);

            return Result.Ok("Grupo removido com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to delete group with cascade. GroupId={GroupId}", groupId);
            throw;
        }
    }

    /// <summary>
    /// Encerra a patota a pedido de quem a administra.
    ///
    /// O <see cref="DeleteAsync"/> não confere quem está pedindo — é o caminho do GodMode, e
    /// manter os dois separados deixa explícito que este aqui tem dono e aquele não.
    ///
    /// Não há trava por quantidade de gente, de propósito: proibir criaria patota-zumbi que
    /// ninguém consegue encerrar. O que protege é a fricção na interface, como em GitHub,
    /// Slack e Discord. O que a trava impede, isso sim, é destruir SEM QUERER — e para isso
    /// a exclusão saiu do fluxo de saída, que era onde o acidente morava.
    /// </summary>
    public async Task<Result> DeleteByAdminAsync(Guid groupId, Guid requestingUserId, CancellationToken cancellationToken)
    {
        var ehAdmin = await _context.Groups
            .AnyAsync(g => g.Id == groupId && g.Admins.Any(a => a.UserId == requestingUserId), cancellationToken);

        if (!ehAdmin)
        {
            // Não distingue "não existe" de "não é admin": quem não administra a patota não
            // precisa descobrir se ela existe.
            return await _context.Groups.AnyAsync(g => g.Id == groupId, cancellationToken)
                ? Result.Fail("Apenas administradores podem encerrar a patota.", ResultStatus.Forbidden)
                : Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);
        }

        _logger.LogWarning(
            "[Groups] Patota {GroupId} encerrada pelo administrador {UserId}.",
            groupId,
            requestingUserId);

        return await DeleteAsync(groupId, cancellationToken);
    }

    /// <summary>
    /// Patotas que ficarão sem nenhuma conta quando <paramref name="userId"/> sair delas.
    ///
    /// "Conta" é o que importa, não "jogador": convidado é só um nome no histórico, não tem
    /// login e não administra nada. Uma patota que fica com convidados e mais ninguém está tão
    /// abandonada quanto uma vazia — ninguém consegue administrá-la, porque adicionar admin
    /// exige já ser admin, e só o GodMode a alcançaria.
    ///
    /// Dois caminhos perguntam isto: sair da patota (PlayerService.LeaveGroupAsync) e excluir
    /// a conta (UserService.DeleteMyAccountAsync). Nos dois o desfecho é o mesmo, então a regra
    /// mora aqui e não em cada um.
    /// </summary>
    public async Task<List<Guid>> FindAbandonedByAsync(Guid userId, CancellationToken cancellationToken)
        => await _context.Groups
            .AsNoTracking()
            .Where(g => !g.Admins.Any(a => a.UserId != userId)
                     && !_context.Players.Any(p => p.GroupId == g.Id
                                                && p.UserId != null
                                                && p.UserId != userId))
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Apaga patotas na ordem que o banco exige, SEM abrir transação: quem chama decide o
    /// escopo. O <see cref="DeleteAsync"/> abre a dele; os dois caminhos de saída já estão
    /// dentro de uma.
    ///
    /// Cinco tabelas apontam para Groups com ON DELETE RESTRICT — Matches, MatchPlayers, Goals,
    /// TeamColors e GroupSettings — e GroupSettings existe para toda patota, então um DELETE
    /// direto em Groups falharia sempre. As demais (Players, Polls, GroupAdmins, GroupInvites,
    /// pagamentos, lançamentos) cascateiam sozinhas. Apagar Matches já leva MatchPlayers, Votes
    /// e Goals pelo CASCADE de MatchId.
    ///
    /// É a única implementação dessa ordem no projeto, de propósito: enquanto existir uma só,
    /// uma FK nova com RESTRICT só precisa ser aprendida aqui.
    ///
    /// Por entidades rastreadas e não ExecuteDelete: é o que já estava em produção, continua
    /// visível para a suíte (o provider InMemory não traduz ExecuteDelete) e o volume é o de
    /// uma patota.
    ///
    /// Um SaveChanges só. A versão anterior salvava a cada nível por medo do RESTRICT, mas o
    /// EF ordena as deleções pela topologia das relações que ele conhece — dependente antes
    /// de principal — e emite os comandos já na ordem certa. Verificado contra Postgres real
    /// com partidas, escalações, cores e configurações no mesmo grupo.
    /// </summary>
    public async Task DeleteManyAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Count == 0)
            return;

        // Partidas levam MatchPlayers, Votes e Goals pelo CASCADE de MatchId; a patota leva
        // GroupAdmins, GroupInvites, Polls e os financeiros. Carregadas aqui só as quatro que
        // o banco se recusa a cascatear (FK GroupId = Restrict), mais a própria patota.
        _context.Matches.RemoveRange(
            await _context.Matches.Where(x => groupIds.Contains(x.GroupId)).ToListAsync(cancellationToken));

        _context.TeamColors.RemoveRange(
            await _context.TeamColors.Where(x => groupIds.Contains(x.GroupId)).ToListAsync(cancellationToken));

        _context.GroupSettings.RemoveRange(
            await _context.GroupSettings.Where(x => groupIds.Contains(x.GroupId)).ToListAsync(cancellationToken));

        _context.Players.RemoveRange(
            await _context.Players.Where(x => groupIds.Contains(x.GroupId)).ToListAsync(cancellationToken));

        _context.Groups.RemoveRange(
            await _context.Groups.Where(x => groupIds.Contains(x.Id)).ToListAsync(cancellationToken));

        await _context.SaveChangesAsync(cancellationToken);
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
                g.Group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.User != null ? p.User.UserName : null, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating, p.AttackRating, p.DefenseRating, p.OverallRating, p.User != null && p.User.ProfilePhotoKey != null ? _images.PublicBaseUrl + "/" + p.User.ProfilePhotoKey : null)).ToList(),
                g.Group.CreatedByUserId
            )
            {
                LogoUrl = g.Group.LogoKey != null
                    ? _images.PublicBaseUrl + "/" + g.Group.LogoKey
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
                g.Group.Players.Select(p => new Domain.Dtos.Players.PlayerDto(p.Id, p.Name, p.UserId, p.User != null ? p.User.UserName : null, p.SkillPoints, p.IsGoalkeeper, p.IsGuest, p.Status, p.GuestStarRating, p.AttackRating, p.DefenseRating, p.OverallRating, p.User != null && p.User.ProfilePhotoKey != null ? _images.PublicBaseUrl + "/" + p.User.ProfilePhotoKey : null)).ToList(),
                g.Group.CreatedByUserId
            )
            {
                LogoUrl = g.Group.LogoKey != null
                    ? _images.PublicBaseUrl + "/" + g.Group.LogoKey
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

    private string? PhotoUrl(UserEntity? user) =>
        user?.ProfilePhotoKey is { } photoKey
            ? _images.BuildPublicUrl(photoKey)
            : null;

    private string? LogoUrl(GroupEntity group) =>
        group.LogoKey is { } logoKey
            ? _images.BuildPublicUrl(logoKey)
            : null;

    public async Task<Result<GroupLogoDto>> SetLogoAsync(
        Guid groupId,
        Stream image,
        CancellationToken cancellationToken)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
            return Result<GroupLogoDto>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        var previousKey = group.LogoKey;

        string objectKey;
        try
        {
            objectKey = await _images.UploadAsync(ImageKind.Logo, groupId, image, cancellationToken);
        }
        catch (InvalidImageException ex)
        {
            return Result<GroupLogoDto>.Fail(ex.Message, ResultStatus.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Groups] Falha ao enviar logo do grupo {GroupId} para o R2.", groupId);
            return Result<GroupLogoDto>.Fail("Falha ao enviar a logo para o storage. Tente novamente.");
        }

        group.SetLogo(objectKey);
        await _context.SaveChangesAsync(cancellationToken);

        // Só depois do commit: se o banco falhar, a logo antiga ainda é a que o grupo exibe,
        // e apagá-la antes deixaria o grupo sem imagem sem nada ter sido trocado.
        await DeletePreviousLogoAsync(previousKey, groupId, cancellationToken);

        var updatedAt = group.LogoUpdatedAt ?? DateTimeOffset.UtcNow;
        return Result<GroupLogoDto>.Ok(new GroupLogoDto(_images.BuildPublicUrl(objectKey), updatedAt));
    }

    public async Task<Result> RemoveLogoAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
            return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        var previousKey = group.LogoKey;

        group.RemoveLogo();
        await _context.SaveChangesAsync(cancellationToken);

        await DeletePreviousLogoAsync(previousKey, groupId, cancellationToken);
        return Result.Ok("Logo removida com sucesso.");
    }

    /// <summary>
    /// Best-effort, mesmo espírito do descarte de clip no MatchService: a linha já foi
    /// gravada, e um objeto órfão de algumas dezenas de KB no bucket é um problema menor do
    /// que derrubar a troca de logo que o admin acabou de fazer.
    /// </summary>
    private async Task DeletePreviousLogoAsync(string? previousKey, Guid groupId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(previousKey)) return;

        try
        {
            await _images.DeleteAsync(previousKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Groups] Logo anterior do grupo {GroupId} não pôde ser removida do R2. Key={ObjectKey}",
                groupId,
                previousKey);
        }
    }

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

                // Convidado não implica sem dono: LeaveAsCreator converte o criador que sai em
                // convidado com SetIsGuest(true), sem ClearUser(). Apontar o convite para esse
                // player transferiria o histórico do dono anterior — gols, mensalidades,
                // cobranças, apostas — para a conta convidada.
                if (guest.UserId is not null && guest.UserId != request.TargetUserId)
                    return Result<GroupInviteDto>.Fail(
                        "Este jogador convidado já pertence a outra conta.",
                        ResultStatus.BadRequest);

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
                i.Group.LogoKey != null ? _images.PublicBaseUrl + "/" + i.Group.LogoKey : null
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

            // CreateInviteAsync já recusa quem é membro, mas o convite fica pendente e a pessoa
            // pode entrar no grupo por outro caminho nesse intervalo. Sem reconferir aqui, o
            // ramo final criaria um SEGUNDO player para o mesmo usuário no mesmo grupo — e não
            // existe índice único em (GroupId, UserId) para barrar, então a duplicata se
            // propagaria para estatística e financeiro.
            var alreadyMember = await _context.Players
                .AnyAsync(p => p.GroupId == invite.GroupId && p.UserId == userId && !p.IsGuest, cancellationToken);

            if (alreadyMember)
                return Result.Fail("Usuário já é membro deste grupo.", ResultStatus.BadRequest);

            PlayerEntity thePlayer;

            if (invite.GuestPlayerId.HasValue)
            {
                // Vincular ao guest player existente
                var player = await _context.Players
                    .FirstOrDefaultAsync(p => p.Id == invite.GuestPlayerId.Value, cancellationToken);

                if (player == null)
                    return Result.Fail("Jogador convidado não encontrado.", ResultStatus.NotFound);

                // Mesma guarda do CreateInviteAsync, repetida aqui de propósito: é esta que
                // protege de fato, porque convites criados antes desta correção já existem
                // pendentes no banco, e a posse do player pode mudar entre criar e aceitar.
                if (player.UserId is not null && player.UserId != userId)
                    return Result.Fail(
                        "Este jogador convidado já pertence a outra conta.",
                        ResultStatus.BadRequest);

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

            // Pelo papel de admin, não por CreatedByUserId. Quem criou é fato histórico e a
            // coluna é anulável: uma patota cujo criador excluiu a conta ficava com criador
            // nulo e NINGUÉM mais conseguia passar por aqui — nem para transferir, nem para
            // encerrar. Administrar é do papel, não de quem fundou.
            if (!group.Admins.Any(a => a.UserId == requestingUserId))
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);

            // Opção 1: encerrar a patota — só quando não há mais ninguém para perder nada.
            //
            // Sair e destruir são atos diferentes, e misturá-los era o risco: quem só queria
            // sair ficava a dois cliques de apagar o histórico de todo mundo, e quando não
            // havia ninguém elegível para promover a exclusão virava a ÚNICA saída oferecida.
            // É o padrão de GitHub e Slack: sair exige transferir; encerrar é ação própria,
            // em outro lugar, com confirmação por digitação.
            if (dto.DeleteGroup)
            {
                var abandonadas = await FindAbandonedByAsync(requestingUserId, cancellationToken);

                if (!abandonadas.Contains(groupId))
                    return Result.Fail(
                        "Há outras pessoas nesta patota. Para sair, promova outro administrador " +
                        "ou transfira a administração. Encerrar a patota é feito nas configurações.",
                        ResultStatus.Conflict);

                await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
                await DeleteManyAsync([groupId], cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return Result.Ok("Patota encerrada: você era a última pessoa nela.");
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
