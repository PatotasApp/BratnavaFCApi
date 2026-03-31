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

    public PlayerService(IRepositoryBase<PlayerEntity> repository, ILogger<PlayerService> logger, AppDbContext context, IPushService push)
    {
        _repository = repository;
        _logger     = logger;
        _context    = context;
        _push       = push;
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

            _context.Players.Add(player);
            await _context.SaveChangesAsync(cancellationToken);

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

            player.Rename(request.Name);
            player.SetSkillPoints(request.SkillPoints);
            player.SetGoalkeeper(request.IsGoalkeeper);
            player.SetIsGuest(request.IsGuest);
            player.SetGuestStarRating(request.GuestStarRating);

            if (request.Status == Status.Inactive && player.Status != Status.Inactive)
                player.Inactivate();
            else if (request.Status == Status.Active && player.Status != Status.Active)
                player.Reactivate();

            _repository.Update(player);
            await _repository.SaveChangesAsync(cancellationToken);

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

    // ── Notificações ──────────────────────────────────────────────────────────

    private Task NotifyAdminsPlayerLeftAsync(Guid groupId, string playerName, CancellationToken ct) =>
        _push.SendToGroupAdminsAsync(
            groupId,
            title: "Jogador saiu do grupo",
            body:  $"{playerName} saiu do grupo.",
            data:  new Dictionary<string, string> { ["type"] = "player_left", ["groupId"] = groupId.ToString() },
            ct);

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
        player.GuestStarRating
    );
}
