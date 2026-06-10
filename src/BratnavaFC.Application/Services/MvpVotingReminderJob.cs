using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MvpVotingReminderJob : IMvpVotingReminderJob
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<MvpVotingReminderJob> _logger;

    public MvpVotingReminderJob(AppDbContext db, IPushService push, ILogger<MvpVotingReminderJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid matchId, Guid groupId, CancellationToken ct = default)
    {
        var match = await _db.Matches
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
        {
            _logger.LogInformation("MvpVotingReminderJob: partida {MatchId} não encontrada — ignorando.", matchId);
            return;
        }

        if (match.Status != MatchStatus.PostGame)
        {
            _logger.LogInformation(
                "MvpVotingReminderJob: partida {MatchId} não está em PostGame (status={Status}) — ignorando.",
                matchId, match.Status);
            return;
        }

        var voterIds = match.Votes.Select(v => v.VoterId).ToHashSet();

        var pendingUserIds = match.Players
            .Where(mp => mp.InviteResponse == InviteResponse.Accepted
                      && mp.Player?.IsGuest != true
                      && mp.Player?.UserId != null
                      && !voterIds.Contains(mp.Id))
            .Select(mp => mp.Player!.UserId!.Value)
            .Distinct()
            .ToList();

        if (pendingUserIds.Count == 0)
        {
            _logger.LogInformation(
                "MvpVotingReminderJob: todos os jogadores já votaram na partida {MatchId} — ignorando.", matchId);
            return;
        }

        await _push.SendToUsersAsync(
            pendingUserIds,
            title: "Votação MVP encerrando! ⏱️",
            body:  "A votação do MVP está prestes a encerrar. Vote agora antes que seja tarde!",
            data: new Dictionary<string, string>
            {
                ["type"]    = "mvp_voting_reminder",
                ["matchId"] = matchId.ToString(),
                ["groupId"] = groupId.ToString(),
            },
            cancellationToken: ct,
            groupId: groupId);

        _logger.LogInformation(
            "MvpVotingReminderJob: lembrete enviado para {Count} jogador(es) que ainda não votaram na partida {MatchId}.",
            pendingUserIds.Count, matchId);
    }
}
