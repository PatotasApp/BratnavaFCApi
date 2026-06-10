using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MatchNoQuorumReminderJob : IMatchNoQuorumReminderJob
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<MatchNoQuorumReminderJob> _logger;

    public MatchNoQuorumReminderJob(AppDbContext db, IPushService push, ILogger<MatchNoQuorumReminderJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid matchId, Guid groupId, CancellationToken ct = default)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
        {
            _logger.LogInformation("MatchNoQuorumReminderJob: partida {MatchId} não encontrada — ignorando.", matchId);
            return;
        }

        if (match.Status != MatchStatus.Acceptation)
        {
            _logger.LogInformation(
                "MatchNoQuorumReminderJob: partida {MatchId} não está em Acceptation (status={Status}) — ignorando.",
                matchId, match.Status);
            return;
        }

        var acceptedCount = await _db.MatchPlayers
            .AsNoTracking()
            .CountAsync(mp => mp.MatchId == matchId && mp.InviteResponse == InviteResponse.Accepted, ct);

        var minPlayers = await _db.GroupSettings
            .AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => (int?)s.MinPlayers)
            .FirstOrDefaultAsync(ct) ?? 5;

        if (acceptedCount >= minPlayers)
        {
            _logger.LogInformation(
                "MatchNoQuorumReminderJob: partida {MatchId} já tem quórum ({Accepted}/{Min}) — ignorando.",
                matchId, acceptedCount, minPlayers);
            return;
        }

        var matchDate = match.PlayedAt.ToString("dd/MM");
        await _push.SendToGroupAdminsAsync(
            groupId,
            title: "Partida sem quórum! ⚠️",
            body:  $"A partida de {matchDate} tem apenas {acceptedCount} de {minPlayers} confirmados. A partida está próxima!",
            data: new Dictionary<string, string>
            {
                ["type"]    = "match_no_quorum",
                ["matchId"] = matchId.ToString(),
                ["groupId"] = groupId.ToString(),
            },
            cancellationToken: ct);

        _logger.LogInformation(
            "MatchNoQuorumReminderJob: alerta de quórum enviado para admins do grupo {GroupId} — {Accepted}/{Min} confirmados.",
            groupId, acceptedCount, minPlayers);
    }
}
