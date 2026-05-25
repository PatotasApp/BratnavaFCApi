using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
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
    private readonly IPushService _push;
    private readonly IMatchService _matchService;

    public PlayerService(
        IRepositoryBase<PlayerEntity> repository,
        ILogger<PlayerService> logger,
        AppDbContext context,
        IPushService push,
        IMatchService matchService)
    {
        _repository   = repository;
        _logger       = logger;
        _context      = context;
        _push         = push;
        _matchService = matchService;
    }

    public async Task<Result<PlayerDto>> CreateAsync(CreatePlayerDto request, CancellationToken cancellationToken)
    {
        try
        {
            var groupExists = await _context.Groups
                .AnyAsync(x => x.Id == request.GroupId, cancellationToken);

            if (!groupExists)
                return Result<PlayerDto>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

            // Apenas valida usuario se nao for convidado e o UserId for informado
            if (!request.IsGuest && request.UserId.HasValue)
            {
                var userExists = await _context.Users
                    .AnyAsync(x => x.Id == request.UserId.Value, cancellationToken);

                if (!userExists)
                    return Result<PlayerDto>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

                var alreadyExists = await _context.Players
                    .AnyAsync(p => p.GroupId == request.GroupId && p.UserId == request.UserId.Value, cancellationToken);

                if (alreadyExists)
                    return Result<PlayerDto>.Fail("Player already exists in the group.", ResultStatus.BadRequest);
            }

            var player = new PlayerEntity(
                request.Name,
                request.UserId,
                request.GroupId,
                request.SkillPoints,
                request.IsGoalkeeper,
                request.IsGuest,
                request.Status);

            if (request.GuestStarRating.HasValue)
                player.SetGuestStarRating(request.GuestStarRating);
            if (request.AttackRating.HasValue)
                player.SetAttackRating(request.AttackRating);
            if (request.DefenseRating.HasValue)
                player.SetDefenseRating(request.DefenseRating);
            if (request.OverallRating.HasValue)
                player.SetOverallRating(request.OverallRating);

            _context.Players.Add(player);
            await _context.SaveChangesAsync(cancellationToken);

            // Auto-sync the new active player into all pre-game matches of this group
            if (player.Status == Status.Active)
                await _matchService.SyncPlayerIntoActiveMatchesAsync(request.GroupId, player.Id, cancellationToken);

            return Result<PlayerDto>.Ok(MapToDto(player), "Jogador criado com sucesso.", ResultStatus.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating player");
            throw;
        }
    }

    public async Task<Result<PlayerDto>> UpdateAsync(Guid playerId, UpdatePlayerDto request, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null)
                return Result<PlayerDto>.Fail("Jogador não encontrado.", ResultStatus.NotFound);

            var wasInactive = player.Status != Status.Active;

            player.Rename(request.Name);
            player.SetSkillPoints(request.SkillPoints);
            player.SetGoalkeeper(request.IsGoalkeeper);
            player.SetIsGuest(request.IsGuest);
            player.SetGuestStarRating(request.GuestStarRating);
            player.SetAttackRating(request.AttackRating);
            player.SetDefenseRating(request.DefenseRating);
            player.SetOverallRating(request.OverallRating);

            if (request.Status == Status.Inactive && player.Status != Status.Inactive)
                player.Inactivate();
            else if (request.Status == Status.Active && player.Status != Status.Active)
                player.Reactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);

            // Sync into pre-game matches when the player transitions from inactive to active
            if (wasInactive && player.Status == Status.Active)
                await _matchService.SyncPlayerIntoActiveMatchesAsync(player.GroupId, player.Id, cancellationToken);

            return Result<PlayerDto>.Ok(MapToDto(player), "Jogador atualizado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player");
            throw;
        }
    }

    public async Task<Result> DeleteAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null)
                return Result.Fail("Jogador não encontrado.", ResultStatus.NotFound);

            // A player who appeared in any match past the pre-game phase is part of match
            // history and must not be deleted (they'd leave orphaned stats / goals / votes).
            var hasMatchHistory = await _context.MatchPlayers
                .AnyAsync(
                    mp => mp.PlayerId == playerId &&
                          mp.Match!.Status != MatchStatus.Created &&
                          mp.Match!.Status != MatchStatus.Acceptation,
                    cancellationToken);

            if (hasMatchHistory)
                return Result.Fail("Jogador possui histórico de partidas e não pode ser excluído. Desative-o em vez de excluir.");

            // Remove any pending pre-game invitations before deleting the player row,
            // otherwise the FK on MatchPlayers.PlayerId would reject the DELETE.
            var preGameInvites = await _context.MatchPlayers
                .Where(mp => mp.PlayerId == playerId)
                .ToListAsync(cancellationToken);

            if (preGameInvites.Count > 0)
                _context.MatchPlayers.RemoveRange(preGameInvites);

            _repository.Remove(player);
            await _repository.SaveChangesAsync(cancellationToken);

            return Result.Ok("Jogador removido com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting player");
            throw;
        }
    }

    public async Task<Result<PlayerDto>> GetByIdAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdAsync(playerId, cancellationToken);
            if (player == null)
                return Result<PlayerDto>.Fail("Jogador não encontrado.", ResultStatus.NotFound);

            return Result<PlayerDto>.Ok(MapToDto(player));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player by id");
            throw;
        }
    }

    public async Task<Result> InactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null)
                return Result.Fail("Jogador não encontrado.", ResultStatus.NotFound);

            player.Inactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);

            return Result.Ok("Jogador atualizado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to inactivate player.");
            throw;
        }
    }

    public async Task<Result> ReactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
            if (player == null)
                return Result.Fail("Jogador não encontrado.", ResultStatus.NotFound);

            player.Reactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);

            // Auto-sync the reactivated player into all pre-game matches of their group
            await _matchService.SyncPlayerIntoActiveMatchesAsync(player.GroupId, player.Id, cancellationToken);

            return Result.Ok("Jogador atualizado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to reactivate player.");
            throw;
        }
    }

    public async Task<Result<IReadOnlyList<MyPlayerDto>>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return Result<IReadOnlyList<MyPlayerDto>>.Fail("UserId is required.", ResultStatus.BadRequest);

        var list = await _context.Players
            .AsNoTracking()
            .Include(p => p.Group)
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Group.Name)
            .Select(p => new MyPlayerDto(
                p.Id,
                p.UserId,
                p.GroupId,
                p.Name,
                p.IsGoalkeeper,
                p.SkillPoints,
                p.Status,
                p.Group.Name,
                p.IsGuest
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<MyPlayerDto>>.Ok(list);
    }

    public async Task<Result> LeaveGroupAsync(Guid playerId, Guid requestingUserId, CancellationToken cancellationToken)
    {
        var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
        if (player == null)
            return Result.Fail("Jogador não encontrado.", ResultStatus.NotFound);

        if (player.UserId != requestingUserId)
            return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);

        var playerName = player.Name;
        var groupId    = player.GroupId;

        player.SetIsGuest(true);
        player.ClearUser();

        _repository.Update(player);
        await _repository.SaveChangesAsync(cancellationToken);

        await NotifyAdminsPlayerLeftAsync(groupId, playerName, cancellationToken);

        return Result.Ok("Jogador atualizado com sucesso.");
    }

    public async Task<Result<IReadOnlyList<BirthdayStatusDto>>> GetBirthdayStatusAsync(
        Guid groupId, CancellationToken cancellationToken)
    {
        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && !p.IsGuest
                     && p.Status == Status.Active
                     && p.UserId != null)
            .Select(p => new
            {
                PlayerId  = p.Id,
                Name      = p.Name,
                // User.BirthDate is DateTimeOffset? — keep as-is, convert after materialisation
                BirthDate = p.User != null ? p.User.BirthDate : (DateTimeOffset?)null,
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = players
            .Select(p =>
            {
                // Convert to DateOnly for day/month arithmetic (use UTC date)
                DateOnly? bd = p.BirthDate.HasValue
                    ? DateOnly.FromDateTime(p.BirthDate.Value.UtcDateTime)
                    : null;

                int? daysUntil = null;
                if (bd.HasValue)
                {
                    // Next occurrence of this birthday from today
                    var thisYear = new DateOnly(today.Year, bd.Value.Month, bd.Value.Day);
                    daysUntil = thisYear >= today
                        ? thisYear.DayNumber - today.DayNumber
                        : new DateOnly(today.Year + 1, bd.Value.Month, bd.Value.Day).DayNumber - today.DayNumber;
                }

                return new { p.PlayerId, p.Name, BirthDate = bd, DaysUntil = daysUntil };
            })
            // Sorted by proximity; players without birthday alphabetically at end
            .OrderBy(p => p.DaysUntil.HasValue ? 0 : 1)
            .ThenBy(p => p.DaysUntil)
            .ThenBy(p => p.Name)
            .Select(p => new BirthdayStatusDto(
                p.PlayerId,
                p.Name,
                p.BirthDate.HasValue,
                p.BirthDate.HasValue ? p.BirthDate.Value.ToString("dd/MM/yyyy") : null,
                p.BirthDate?.Month,
                p.BirthDate?.Day
            ))
            .ToList();

        return Result<IReadOnlyList<BirthdayStatusDto>>.Ok(result);
    }

    public async Task<Result> RemoveFromGroupAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var player = await _repository.GetByIdIncludingInactiveAsync(playerId, cancellationToken);
        if (player == null)
            return Result.Fail("Jogador não encontrado.", ResultStatus.NotFound);

        if (player.IsGuest && player.UserId == null)
            return Result.Fail("Jogador já é convidado sem conta vinculada.", ResultStatus.BadRequest);

        var playerName = player.Name;
        var groupId    = player.GroupId;

        player.SetIsGuest(true);
        player.ClearUser();

        _repository.Update(player);
        await _repository.SaveChangesAsync(cancellationToken);

        await NotifyAdminsPlayerRemovedAsync(groupId, playerName, cancellationToken);

        return Result.Ok("Jogador removido da patota.");
    }

    // ── Notificações ──────────────────────────────────────────────────────────

    private Task NotifyAdminsPlayerLeftAsync(Guid groupId, string playerName, CancellationToken ct) =>
        _push.SendToGroupAdminsAsync(
            groupId,
            title: "Jogador saiu do grupo",
            body:  $"{playerName} saiu do grupo.",
            data:  new Dictionary<string, string> { ["type"] = "player_left", ["groupId"] = groupId.ToString() },
            ct);

    private Task NotifyAdminsPlayerRemovedAsync(Guid groupId, string playerName, CancellationToken ct) =>
        _push.SendToGroupAdminsAsync(
            groupId,
            title: "Jogador removido da patota",
            body:  $"{playerName} foi removido e voltou a ser convidado.",
            data:  new Dictionary<string, string> { ["type"] = "player_removed", ["groupId"] = groupId.ToString() },
            ct);

    // ── Toggle goleiro/linha ──────────────────────────────────────────────────

    public async Task<Result<PlayerDto>> ToggleGoalkeeperAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var player = await _repository.GetByIdAsync(playerId, cancellationToken);
        if (player == null)
            return Result<PlayerDto>.Fail("Jogador não encontrado.", ResultStatus.NotFound);

        player.SetGoalkeeper(!player.IsGoalkeeper);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<PlayerDto>.Ok(MapToDto(player));
    }

    // ── Mapeamento ────────────────────────────────────────────────────────────

    private static PlayerDto MapToDto(PlayerEntity player) => new(
        player.Id,
        player.Name,
        player.UserId,
        string.Empty,
        player.SkillPoints,
        player.IsGoalkeeper,
        player.IsGuest,
        player.Status,
        player.GuestStarRating,
        player.AttackRating,
        player.DefenseRating,
        player.OverallRating
    );
}
