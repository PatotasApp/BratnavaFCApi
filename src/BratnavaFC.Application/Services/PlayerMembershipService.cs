using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class PlayerMembershipService : IPlayerMembershipService
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<PlayerMembershipService> _logger;

    public PlayerMembershipService(
        AppDbContext db,
        IPushService push,
        ILogger<PlayerMembershipService> logger)
    {
        _db = db;
        _push = push;
        _logger = logger;
    }

    public async Task<Result<PlayerUnlinkResult>> UnlinkPlayerFromGroupAsync(
        Guid playerId,
        PlayerUnlinkReason reason,
        Guid? requestingUserId,
        CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (player is null)
            return Result<PlayerUnlinkResult>.Fail("Jogador n\u00e3o encontrado.", ResultStatus.NotFound);

        if (reason == PlayerUnlinkReason.SelfLeave && player.UserId != requestingUserId)
            return Result<PlayerUnlinkResult>.Fail("Sem permiss\u00e3o para esta opera\u00e7\u00e3o.", ResultStatus.Forbidden);

        if (player.IsGuest && player.UserId is null)
            return Result<PlayerUnlinkResult>.Fail("Jogador j\u00e1 \u00e9 convidado sem conta vinculada.", ResultStatus.BadRequest);

        var removedUserId = player.UserId;
        var result = new PlayerUnlinkResult(player.Id, player.GroupId, player.Name, removedUserId);

        player.SetIsGuest(true);
        player.ClearUser();

        if (removedUserId.HasValue)
        {
            await RemoveGroupRolesAsync(player.GroupId, removedUserId.Value, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(result, reason, cancellationToken);

        return Result<PlayerUnlinkResult>.Ok(result);
    }

    public async Task<Result<IReadOnlyList<PlayerUnlinkResult>>> UnlinkUserFromAllGroupsAsync(
        Guid userId,
        PlayerUnlinkReason reason,
        CancellationToken cancellationToken)
    {
        var playerIds = await _db.Players
            .IgnoreQueryFilters()
            .Where(p => p.UserId == userId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var results = new List<PlayerUnlinkResult>();
        foreach (var playerId in playerIds)
        {
            var result = await UnlinkPlayerFromGroupAsync(playerId, reason, userId, cancellationToken);
            if (!result.Success)
                return Result<IReadOnlyList<PlayerUnlinkResult>>.Fail(result.Error ?? "Erro ao desvincular jogador.", result.Status);
            results.Add(result.Data!);
        }

        return Result<IReadOnlyList<PlayerUnlinkResult>>.Ok(results);
    }

    private async Task RemoveGroupRolesAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        _db.GroupAdmins.RemoveRange(await _db.GroupAdmins
            .Where(x => x.GroupId == groupId && x.UserId == userId)
            .ToListAsync(ct));

        _db.GroupFinanceiros.RemoveRange(await _db.GroupFinanceiros
            .Where(x => x.GroupId == groupId && x.UserId == userId)
            .ToListAsync(ct));
    }

    private async Task NotifyAsync(PlayerUnlinkResult result, PlayerUnlinkReason reason, CancellationToken ct)
    {
        try
        {
            switch (reason)
            {
                case PlayerUnlinkReason.SelfLeave:
                    await NotifyAdminsPlayerLeftAsync(result.GroupId, result.PlayerName, ct);
                    break;
                case PlayerUnlinkReason.AdminRemove:
                    await NotifyAdminsPlayerRemovedAsync(result.GroupId, result.PlayerName, ct);
                    if (result.RemovedUserId.HasValue)
                        await NotifyRemovedPlayerAsync(result.RemovedUserId.Value, result.GroupId, ct);
                    break;
                case PlayerUnlinkReason.AccountDeletion:
                    await NotifyAdminsPlayerLeftAsync(result.GroupId, result.PlayerName, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar notificaÃ§Ã£o de desvinculaÃ§Ã£o do jogador {PlayerId}.", result.PlayerId);
        }
    }

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
            body:  $"{playerName} foi removido da patota.",
            data:  new Dictionary<string, string> { ["type"] = "player_removed", ["groupId"] = groupId.ToString() },
            ct);

    private Task NotifyRemovedPlayerAsync(Guid userId, Guid groupId, CancellationToken ct) =>
        _push.SendToUserAsync(
            userId,
            title: "VocÃª foi removido da patota",
            body:  "Seu vÃ­nculo com a patota foi removido pelo administrador.",
            data:  new Dictionary<string, string> { ["type"] = "removed_from_group", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);
}
