using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Discovery;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class DiscoveryService : IDiscoveryService
{
    private readonly AppDbContext _db;
    private readonly IImageStorageService _images;
    private readonly IPushService _push;

    public DiscoveryService(AppDbContext db, IImageStorageService images, IPushService push)
    {
        _db = db;
        _images = images;
        _push = push;
    }

    public async Task<Result<PagedResultDto<GroupSearchItemDto>>> SearchGroupsAsync(Guid requesterId, string? query, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Page(page, pageSize);
        var term = (query ?? "").Trim().ToLower();
        if (term.Length == 0)
            return Result<PagedResultDto<GroupSearchItemDto>>.Ok(EmptyPage<GroupSearchItemDto>(page, pageSize));
        var groups = _db.Groups.AsNoTracking().Where(g => g.Status == Status.Active && g.IsPublic &&
            !g.Players.Any(p => p.UserId == requesterId && !p.IsGuest && p.Status == Status.Active) &&
            !_db.GroupAdmins.Any(a => a.GroupId == g.Id && a.UserId == requesterId));
        groups = groups.Where(g => g.Name.ToLower().Contains(term) || g.Code.ToLower().Contains(term));
        var total = await groups.CountAsync(ct);
        var rows = await groups.OrderBy(g => g.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(g => new { g.Id, g.Name, g.Code, g.LogoKey, g.ProfileCity, g.IsAcceptingPlayers,
                Count = g.Players.Count(p => !p.IsGuest && p.Status == Status.Active) }).ToListAsync(ct);
        return Result<PagedResultDto<GroupSearchItemDto>>.Ok(new()
        {
            Page = page, PageSize = pageSize, Total = total,
            Items = rows.Select(x => new GroupSearchItemDto(x.Id, x.Name, x.Code, Image(x.LogoKey),
                x.ProfileCity, x.IsAcceptingPlayers, x.Count)).ToList()
        });
    }

    public async Task<Result<PagedResultDto<PlayerSearchItemDto>>> SearchPlayersAsync(Guid requesterId, string? query, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Page(page, pageSize);
        var term = (query ?? "").Trim().ToLower();
        if (term.Length == 0)
            return Result<PagedResultDto<PlayerSearchItemDto>>.Ok(EmptyPage<PlayerSearchItemDto>(page, pageSize));
        var myGroups = _db.Players.AsNoTracking().Where(p => p.UserId == requesterId).Select(p => p.GroupId);
        var visible = _db.Users.AsNoTracking().Where(u => u.Status == Status.Active && u.Id != requesterId &&
            (u.ProfileVisibility == ProfileVisibility.AuthenticatedUsers ||
             (u.ProfileVisibility == ProfileVisibility.SharedPatotas &&
              u.Players.Any(p => myGroups.Contains(p.GroupId)))));
        visible = visible.Where(u => u.UserName.ToLower().Contains(term) ||
            (u.FirstName + " " + u.LastName).ToLower().Contains(term));
        var total = await visible.CountAsync(ct);
        var rows = await visible.OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName, u.ProfilePhotoKey,
                Goalkeeper = u.Players.Where(p => !p.IsGuest).Select(p => (bool?)p.IsGoalkeeper).FirstOrDefault() })
            .ToListAsync(ct);
        return Result<PagedResultDto<PlayerSearchItemDto>>.Ok(new()
        {
            Page = page, PageSize = pageSize, Total = total,
            Items = rows.Select(x => new PlayerSearchItemDto(x.Id, $"{x.FirstName} {x.LastName}".Trim(),
                x.UserName, Image(x.ProfilePhotoKey), x.Goalkeeper.HasValue ? x.Goalkeeper.Value ? "Goleiro" : "Linha" : null)).ToList()
        });
    }

    public async Task<Result<GroupProfileDto>> GetGroupProfileAsync(Guid groupId, Guid requesterId, CancellationToken ct)
    {
        var group = await _db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId && g.Status == Status.Active, ct);
        if (group is null) return Result<GroupProfileDto>.Fail("Patota não encontrada.", ResultStatus.NotFound);
        var isMember = await _db.Players.AnyAsync(p => p.GroupId == groupId && p.UserId == requesterId, ct);
        var isAdmin = await _db.GroupAdmins.AnyAsync(a => a.GroupId == groupId && a.UserId == requesterId, ct);
        if (!group.IsPublic && !isMember && !isAdmin)
            return Result<GroupProfileDto>.Fail("Esta patota é privada.", ResultStatus.NotFound);

        var settings = await _db.GroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        var applicationByMatch = await _db.MatchApplications.AsNoTracking()
            .Where(x => x.GroupId == groupId && x.UserId == requesterId &&
                (x.Status == MatchApplicationStatus.PendingAdmin || x.Status == MatchApplicationStatus.PendingPlayer || x.Status == MatchApplicationStatus.Accepted))
            .ToDictionaryAsync(x => x.MatchId, x => x.Status, ct);
        // Candidaturas públicas dependem de a patota estar aberta. O administrador,
        // porém, pode convidar diretamente um jogador para conhecer a patota mesmo
        // quando ela não recebe candidaturas espontâneas.
        var matches = group.IsAcceptingPlayers || isAdmin
            ? await _db.Matches.AsNoTracking().Where(m => m.GroupId == groupId && m.Status == MatchStatus.Acceptation && m.PlayedAt >= DateTime.UtcNow)
                .OrderBy(m => m.PlayedAt).Take(10).Select(m => new { m.Id, m.PlayedAt, m.PlaceName }).ToListAsync(ct)
            : [];
        var matchIds = matches.Select(x => x.Id).ToList();
        var openings = await _db.MatchOpenings.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId) && x.IsActive)
            .Select(x => new
            {
                x.Id, x.MatchId, x.Slots, x.Position,
                Reserved = _db.MatchApplications.Count(a => a.OpeningId == x.Id &&
                    (a.Status == MatchApplicationStatus.PendingPlayer || a.Status == MatchApplicationStatus.Accepted))
            }).ToListAsync(ct);
        var members = new List<GroupProfileMemberDto>();
        if (group.ShowMembersOnProfile || isMember || isAdmin)
        {
            var memberRows = await _db.Players.AsNoTracking().Where(p => p.GroupId == groupId && !p.IsGuest && p.UserId != null && p.Status == Status.Active)
                .Select(p => new { UserId = p.UserId!.Value, p.Name, p.User!.UserName, p.User.ProfilePhotoKey }).OrderBy(p => p.Name).ToListAsync(ct);
            members = memberRows.Select(x => new GroupProfileMemberDto(x.UserId, x.Name, x.UserName, Image(x.ProfilePhotoKey))).ToList();
        }
        return Result<GroupProfileDto>.Ok(new()
        {
            GroupId = group.Id, Name = group.Name, Code = group.Code, LogoUrl = Image(group.LogoKey),
            Description = group.ProfileDescription, City = group.ProfileCity,
            DefaultDayOfWeek = settings?.DefaultDayOfWeek, DefaultKickoffTime = settings?.DefaultKickoffTime,
            MemberCount = await _db.Players.CountAsync(p => p.GroupId == groupId && !p.IsGuest && p.Status == Status.Active, ct),
            IsPublic = group.IsPublic, IsAcceptingPlayers = group.IsAcceptingPlayers, ShowMembers = group.ShowMembersOnProfile,
            Members = members,
            UpcomingMatches = matches.Select(m =>
            {
                applicationByMatch.TryGetValue(m.Id, out var appStatus);
                var canSeePlace = isMember || isAdmin || appStatus is MatchApplicationStatus.PendingPlayer or MatchApplicationStatus.Accepted;
                return new PublicMatchDto(m.Id, m.PlayedAt, canSeePlace ? m.PlaceName : null,
                    applicationByMatch.ContainsKey(m.Id) ? appStatus : null,
                    openings.Where(x => x.MatchId == m.Id && x.Reserved < x.Slots)
                        .Select(x => new MatchOpeningDto(x.Id, x.Slots, x.Slots - x.Reserved, x.Position)).ToList());
            }).ToList()
        });
    }

    public async Task<Result> UpdateGroupProfileAsync(Guid groupId, UpdateGroupProfileDto dto, CancellationToken ct)
    {
        var group = await _db.Groups.FindAsync([groupId], ct);
        if (group is null) return Result.Fail("Patota não encontrada.", ResultStatus.NotFound);
        group.UpdateProfile(dto.IsPublic, dto.IsAcceptingPlayers, dto.ShowMembers, dto.Description, dto.City);
        await _db.SaveChangesAsync(ct);
        return Result.Ok("Perfil da patota atualizado.");
    }

    public async Task<Result<string>> RegenerateGroupCodeAsync(Guid groupId, CancellationToken ct)
    {
        var group = await _db.Groups.FindAsync([groupId], ct);
        if (group is null) return Result<string>.Fail("Patota não encontrada.", ResultStatus.NotFound);
        string code;
        do code = GroupEntity.GenerateCode(); while (await _db.Groups.AnyAsync(g => g.Code == code, ct));
        group.RegenerateCode(code);
        await _db.SaveChangesAsync(ct);
        return Result<string>.Ok(code, "Novo código gerado.");
    }

    public async Task<Result<MatchOpeningDto>> CreateOpeningAsync(Guid groupId, Guid matchId, UpsertMatchOpeningDto dto, CancellationToken ct)
    {
        if (await MatchUnavailable(matchId, ct, groupId))
            return Result<MatchOpeningDto>.Fail("A partida não está na fase de aceitação.", ResultStatus.Conflict);
        var opening = new MatchOpeningEntity(groupId, matchId, dto.Slots, dto.Position);
        _db.MatchOpenings.Add(opening);
        await _db.SaveChangesAsync(ct);
        return Result<MatchOpeningDto>.Ok(new(opening.Id, opening.Slots, opening.Slots, opening.Position), "Vaga publicada.", ResultStatus.Created);
    }

    public async Task<Result<MatchOpeningDto>> UpdateOpeningAsync(Guid groupId, Guid openingId, UpsertMatchOpeningDto dto, CancellationToken ct)
    {
        var opening = await _db.MatchOpenings.FirstOrDefaultAsync(x => x.Id == openingId && x.GroupId == groupId && x.IsActive, ct);
        if (opening is null) return Result<MatchOpeningDto>.Fail("Vaga não encontrada.", ResultStatus.NotFound);
        if (await MatchUnavailable(opening.MatchId, ct)) return Result<MatchOpeningDto>.Fail("A partida não está mais em aceitação.", ResultStatus.Conflict);
        var reserved = await ReservedSlots(opening.Id, ct);
        if (dto.Slots < reserved) return Result<MatchOpeningDto>.Fail($"Há {reserved} jogador(es) já aprovado(s) nesta vaga.", ResultStatus.Conflict);
        opening.Update(dto.Slots, dto.Position);
        await _db.SaveChangesAsync(ct);
        return Result<MatchOpeningDto>.Ok(new(opening.Id, opening.Slots, opening.Slots - reserved, opening.Position), "Vaga atualizada.");
    }

    public async Task<Result> DeleteOpeningAsync(Guid groupId, Guid openingId, CancellationToken ct)
    {
        var opening = await _db.MatchOpenings.FirstOrDefaultAsync(x => x.Id == openingId && x.GroupId == groupId && x.IsActive, ct);
        if (opening is null) return Result.Fail("Vaga não encontrada.", ResultStatus.NotFound);
        opening.Close();
        var pending = await _db.MatchApplications.Where(x => x.OpeningId == openingId &&
            (x.Status == MatchApplicationStatus.PendingAdmin || x.Status == MatchApplicationStatus.PendingPlayer)).ToListAsync(ct);
        foreach (var application in pending) application.Cancel();
        await _db.SaveChangesAsync(ct);
        foreach (var application in pending)
            await _push.SendToUserAsync(application.UserId, "Vaga encerrada", "A vaga desta partida foi encerrada pelo administrador.", groupId: groupId, cancellationToken: ct);
        return Result.Ok("Vaga removida.");
    }

    public async Task<Result<MatchApplicationDto>> ApplyAsync(Guid groupId, Guid userId, CreateMatchApplicationDto dto, CancellationToken ct)
    {
        var validation = await ValidateApplicationAsync(groupId, dto.MatchId, userId, requireOpenGroup: true, ct);
        if (validation is not null) return Result<MatchApplicationDto>.Fail(validation.Value.Error, validation.Value.Status);
        var application = new MatchApplicationEntity(groupId, dto.MatchId, userId,
            MatchApplicationSource.PlayerRequest, message: dto.Message);
        _db.MatchApplications.Add(application);
        await _db.SaveChangesAsync(ct);
        await _push.SendToGroupAdminsAsync(groupId, "Novo jogador interessado", "Um jogador se candidatou para a próxima partida.",
            new() { ["type"] = "match_application", ["groupId"] = groupId.ToString(), ["matchId"] = dto.MatchId.ToString() }, ct);
        return Result<MatchApplicationDto>.Ok(await MapAsync(application.Id, ct), "Candidatura enviada.", ResultStatus.Created);
    }

    public async Task<Result<List<MatchApplicationDto>>> GetGroupApplicationsAsync(Guid groupId, CancellationToken ct) =>
        Result<List<MatchApplicationDto>>.Ok(await MapQuery(_db.MatchApplications.AsNoTracking().Where(x => x.GroupId == groupId &&
            (x.Status == MatchApplicationStatus.PendingAdmin || x.Status == MatchApplicationStatus.PendingPlayer))).ToListAsync(ct));

    public async Task<Result<List<MatchApplicationDto>>> GetMyApplicationsAsync(Guid userId, CancellationToken ct) =>
        Result<List<MatchApplicationDto>>.Ok(await MapQuery(_db.MatchApplications.AsNoTracking().Where(x => x.UserId == userId &&
            (x.Status == MatchApplicationStatus.PendingAdmin || x.Status == MatchApplicationStatus.PendingPlayer))).ToListAsync(ct));

    public async Task<Result<List<GuestCandidateDto>>> GetGuestCandidatesAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var rows = await _db.Players.AsNoTracking().Where(p => p.GroupId == groupId && p.IsGuest && (p.UserId == null || p.UserId == userId))
            .OrderByDescending(p => p.UserId == userId).ThenBy(p => p.Name)
            .Select(p => new GuestCandidateDto(p.Id, p.Name, p.GuestOrigin ?? GuestOrigin.Manual, p.UserId == userId)).ToListAsync(ct);
        return Result<List<GuestCandidateDto>>.Ok(rows);
    }

    public async Task<Result> ApproveAsync(Guid groupId, Guid applicationId, ApproveMatchApplicationDto dto, CancellationToken ct)
    {
        var app = await LoadPendingAsync(groupId, applicationId, MatchApplicationStatus.PendingAdmin, ct);
        if (app is null) return Result.Fail("Candidatura não encontrada.", ResultStatus.NotFound);
        if (await MatchUnavailable(app.MatchId, ct)) { app.Cancel(); await _db.SaveChangesAsync(ct); return Result.Fail("A partida não está mais aceitando jogadores.", ResultStatus.Conflict); }
        if (app.OpeningId.HasValue)
        {
            var opening = await _db.MatchOpenings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == app.OpeningId && x.IsActive, ct);
            if (opening is null || await ReservedSlots(opening.Id, ct) >= opening.Slots)
                return Result.Fail("Esta vaga já foi preenchida ou encerrada.", ResultStatus.Conflict);
        }
        var guestError = await ValidateGuestAsync(groupId, app.UserId, dto.GuestPlayerId, ct);
        if (guestError is not null) return Result.Fail(guestError);
        app.ApproveByAdmin(dto.GuestPlayerId, dto.Reason);
        await _db.SaveChangesAsync(ct);
        await NotifyPlayerInvite(app, ct);
        return Result.Ok("Candidatura aprovada; aguardando confirmação do jogador.");
    }

    public async Task<Result<MatchApplicationDto>> InviteAsync(Guid groupId, Guid adminUserId, CreateAdminMatchInviteDto dto, CancellationToken ct)
    {
        var validation = await ValidateApplicationAsync(groupId, dto.MatchId, dto.UserId, requireOpenGroup: false, ct);
        if (validation is not null) return Result<MatchApplicationDto>.Fail(validation.Value.Error, validation.Value.Status);
        var guestError = await ValidateGuestAsync(groupId, dto.UserId, dto.GuestPlayerId, ct);
        if (guestError is not null) return Result<MatchApplicationDto>.Fail(guestError);
        if (dto.OpeningId.HasValue)
        {
            var opening = await _db.MatchOpenings.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == dto.OpeningId && x.GroupId == groupId && x.MatchId == dto.MatchId && x.IsActive, ct);
            if (opening is null) return Result<MatchApplicationDto>.Fail("Vaga não encontrada.", ResultStatus.NotFound);
            if (await ReservedSlots(opening.Id, ct) >= opening.Slots)
                return Result<MatchApplicationDto>.Fail("Esta vaga já foi preenchida.", ResultStatus.Conflict);
        }
        var app = new MatchApplicationEntity(groupId, dto.MatchId, dto.UserId,
            MatchApplicationSource.AdminInvite, adminUserId, dto.OpeningId, dto.Message);
        app.SelectGuest(dto.GuestPlayerId);
        _db.MatchApplications.Add(app);
        await _db.SaveChangesAsync(ct);
        await NotifyPlayerInvite(app, ct);
        return Result<MatchApplicationDto>.Ok(await MapAsync(app.Id, ct), "Convite enviado.", ResultStatus.Created);
    }

    public async Task<Result> RejectByAdminAsync(Guid groupId, Guid applicationId, RejectMatchApplicationDto dto, CancellationToken ct)
    {
        var app = await LoadPendingAsync(groupId, applicationId, MatchApplicationStatus.PendingAdmin, ct);
        if (app is null) return Result.Fail("Candidatura não encontrada.", ResultStatus.NotFound);
        app.Reject(dto.Reason); await _db.SaveChangesAsync(ct);
        var detail = string.IsNullOrWhiteSpace(dto.Reason) ? "" : $" Motivo: {dto.Reason.Trim()}";
        await _push.SendToUserAsync(app.UserId, "Candidatura atualizada", $"A patota não conseguiu aceitar sua participação nesta partida.{detail}", groupId: groupId, cancellationToken: ct);
        return Result.Ok("Candidatura recusada.");
    }

    public async Task<Result> AcceptByPlayerAsync(Guid applicationId, Guid userId, CancellationToken ct)
    {
        var app = await _db.MatchApplications.Include(x => x.Match).FirstOrDefaultAsync(x => x.Id == applicationId && x.UserId == userId && x.Status == MatchApplicationStatus.PendingPlayer, ct);
        if (app is null) return Result.Fail("Convite não encontrado.", ResultStatus.NotFound);
        if (app.Match.Status != MatchStatus.Acceptation) { app.Cancel(); await _db.SaveChangesAsync(ct); return Result.Fail("A partida já começou; o convite foi cancelado.", ResultStatus.Conflict); }
        var player = await _db.Players.FirstOrDefaultAsync(p => p.GroupId == app.GroupId && p.UserId == userId && p.IsGuest, ct);
        if (player is null && app.GuestPlayerId.HasValue)
        {
            player = await _db.Players.FirstOrDefaultAsync(p => p.Id == app.GuestPlayerId && p.GroupId == app.GroupId && p.IsGuest && (p.UserId == null || p.UserId == userId), ct);
            player?.SetUser(userId);
        }
        if (player is null)
        {
            var user = await _db.Users.FindAsync([userId], ct);
            if (user is null) return Result.Fail("Jogador não encontrado.", ResultStatus.NotFound);
            player = new PlayerEntity(user.DisplayName, userId, app.GroupId, 0, false, true, Status.Active, GuestOrigin.ExternalPlayer);
            _db.Players.Add(player);
        }
        var matchPlayer = await _db.MatchPlayers.FirstOrDefaultAsync(mp => mp.MatchId == app.MatchId && mp.PlayerId == player.Id, ct);
        if (matchPlayer is null)
        {
            matchPlayer = new MatchPlayerEntity(player.Id);
            matchPlayer.AssignToMatch(app.Match);
            matchPlayer.AssignToPlayer(player);
            _db.MatchPlayers.Add(matchPlayer);
        }
        matchPlayer.AcceptInvite();
        app.AcceptByPlayer(player.Id);
        await _db.SaveChangesAsync(ct);
        await _push.SendToGroupAdminsAsync(app.GroupId, "Jogador confirmado", $"{player.Name} confirmou presença na partida.", cancellationToken: ct);
        return Result.Ok("Presença confirmada.");
    }

    public async Task<Result> RejectByPlayerAsync(Guid applicationId, Guid userId, CancellationToken ct)
    {
        var app = await _db.MatchApplications.FirstOrDefaultAsync(x => x.Id == applicationId && x.UserId == userId && x.Status == MatchApplicationStatus.PendingPlayer, ct);
        if (app is null) return Result.Fail("Convite não encontrado.", ResultStatus.NotFound);
        app.Reject(); await _db.SaveChangesAsync(ct);
        await _push.SendToGroupAdminsAsync(app.GroupId, "Convite recusado", "O jogador não poderá participar desta partida.", cancellationToken: ct);
        return Result.Ok("Convite recusado.");
    }

    private async Task<(string Error, ResultStatus Status)?> ValidateApplicationAsync(Guid groupId, Guid matchId, Guid userId, bool requireOpenGroup, CancellationToken ct)
    {
        var group = await _db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null || group.Status != Status.Active) return ("Patota não encontrada.", ResultStatus.NotFound);
        if (requireOpenGroup && (!group.IsPublic || !group.IsAcceptingPlayers)) return ("Esta patota não está aceitando jogadores.", ResultStatus.Forbidden);
        if (await _db.Players.AnyAsync(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest, ct)) return ("Você já faz parte desta patota.", ResultStatus.Conflict);
        if (await MatchUnavailable(matchId, ct, groupId)) return ("A partida não está disponível.", ResultStatus.Conflict);
        if (await _db.MatchApplications.AnyAsync(x => x.MatchId == matchId && x.UserId == userId &&
            (x.Status == MatchApplicationStatus.PendingAdmin || x.Status == MatchApplicationStatus.PendingPlayer || x.Status == MatchApplicationStatus.Accepted), ct))
            return ("Já existe uma candidatura ou convite para esta partida.", ResultStatus.Conflict);
        return null;
    }

    private async Task<string?> ValidateGuestAsync(Guid groupId, Guid userId, Guid? guestPlayerId, CancellationToken ct)
    {
        if (!guestPlayerId.HasValue) return null;
        return await _db.Players.AnyAsync(p => p.Id == guestPlayerId && p.GroupId == groupId && p.IsGuest && (p.UserId == null || p.UserId == userId), ct)
            ? null : "O convidado selecionado não pode ser vinculado a este jogador.";
    }

    private async Task<bool> MatchUnavailable(Guid matchId, CancellationToken ct, Guid? groupId = null) =>
        !await _db.Matches.AnyAsync(m => m.Id == matchId && (!groupId.HasValue || m.GroupId == groupId) && m.Status == MatchStatus.Acceptation && m.PlayedAt >= DateTime.UtcNow, ct);

    private Task<MatchApplicationEntity?> LoadPendingAsync(Guid groupId, Guid id, MatchApplicationStatus status, CancellationToken ct) =>
        _db.MatchApplications.FirstOrDefaultAsync(x => x.Id == id && x.GroupId == groupId && x.Status == status, ct);

    private Task<int> ReservedSlots(Guid openingId, CancellationToken ct) =>
        _db.MatchApplications.CountAsync(x => x.OpeningId == openingId &&
            (x.Status == MatchApplicationStatus.PendingPlayer || x.Status == MatchApplicationStatus.Accepted), ct);

    private async Task NotifyPlayerInvite(MatchApplicationEntity app, CancellationToken ct)
    {
        var match = await _db.Matches.AsNoTracking().FirstAsync(x => x.Id == app.MatchId, ct);
        var note = app.DecisionReason ?? app.Message;
        var detail = string.IsNullOrWhiteSpace(note) ? "" : $" Mensagem: {note}";
        await _push.SendToUserAsync(app.UserId, "Convite para jogar", $"Você foi aceito para jogar em {match.PlayedAt:dd/MM 'às' HH:mm}, em {match.PlaceName}. Confirme sua presença.{detail}",
            new() { ["type"] = "match_application_invite", ["applicationId"] = app.Id.ToString(), ["matchId"] = app.MatchId.ToString() }, ct, app.GroupId);
    }

    private async Task<MatchApplicationDto> MapAsync(Guid id, CancellationToken ct) =>
        (await MapQuery(_db.MatchApplications.AsNoTracking().Where(x => x.Id == id)).SingleAsync(ct));

    private IQueryable<MatchApplicationDto> MapQuery(IQueryable<MatchApplicationEntity> query) => query
        .OrderByDescending(x => x.CreateDate)
        .Select(x => new MatchApplicationDto(x.Id, x.GroupId, x.Group.Name, x.MatchId, x.Match.PlayedAt,
            x.Status == MatchApplicationStatus.PendingPlayer || x.Status == MatchApplicationStatus.Accepted ? x.Match.PlaceName : null,
            x.UserId, (x.User.FirstName + " " + x.User.LastName).Trim(), x.User.UserName,
            x.User.ProfilePhotoKey == null ? null : _images.PublicBaseUrl + "/" + x.User.ProfilePhotoKey,
            x.Source, x.Status, x.GuestPlayerId, x.OpeningId, x.Opening == null ? null : x.Opening.Position,
            x.Message, x.DecisionReason, x.CreateDate));

    private string? Image(string? key) => key is null ? null : _images.BuildPublicUrl(key);
    private static PagedResultDto<T> EmptyPage<T>(int page, int pageSize) => new()
    {
        Page = page,
        PageSize = pageSize,
        Total = 0,
        Items = []
    };
    private static (int, int) Page(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 50));
}
