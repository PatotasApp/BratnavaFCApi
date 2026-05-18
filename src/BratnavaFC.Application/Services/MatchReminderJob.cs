using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MatchReminderJob : IMatchReminderJob
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<MatchReminderJob> _logger;

    public MatchReminderJob(AppDbContext db, IPushService push, ILogger<MatchReminderJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid matchId, Guid groupId, string triggerType, CancellationToken ct = default)
    {
        var match = await _db.Matches
            .Include(m => m.Players)
                .ThenInclude(mp => mp.Player)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        // Se a partida não existe ou já saiu da fase de aceitação, não há o que fazer
        if (match is null)
        {
            _logger.LogInformation("MatchReminderJob ({TriggerType}): partida {MatchId} não encontrada — ignorando.", triggerType, matchId);
            return;
        }

        if (match.Status != MatchStatus.Acceptation)
        {
            _logger.LogInformation(
                "MatchReminderJob ({TriggerType}): partida {MatchId} não está em Acceptation (status={Status}) — ignorando.",
                triggerType, matchId, match.Status);
            return;
        }

        // Apenas jogadores reais (não-convidados, com UserId) que ainda não responderam
        var pendingUserIds = match.Players
            .Where(mp =>
                mp.InviteResponse == InviteResponse.None &&
                mp.Player?.IsGuest != true &&
                mp.Player?.UserId is not null)
            .Select(mp => mp.Player!.UserId!.Value)
            .Distinct()
            .ToList();

        if (pendingUserIds.Count == 0)
        {
            _logger.LogInformation(
                "MatchReminderJob ({TriggerType}): partida {MatchId} não tem jogadores pendentes — ignorando.",
                triggerType, matchId);
            return;
        }

        var (title, body) = triggerType == "24h"
            ? ("Partida amanhã! ⚽", "Você ainda não confirmou presença. Responda agora antes que feche!")
            : ("Partida em 2 horas! ⚽", "Última chance: confirme ou recuse sua presença na partida de hoje.");

        var data = new Dictionary<string, string>
        {
            ["type"]    = "match_invite_reminder",
            ["matchId"] = matchId.ToString(),
            ["groupId"] = groupId.ToString(),
        };

        foreach (var userId in pendingUserIds)
        {
            await _push.SendToUserAsync(userId, title, body, data, ct, groupId: groupId);
        }

        _logger.LogInformation(
            "MatchReminderJob ({TriggerType}): lembrete enviado para {Count} jogador(es) da partida {MatchId}.",
            triggerType, pendingUserIds.Count, matchId);
    }
}
