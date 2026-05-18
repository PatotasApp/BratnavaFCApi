using BratnavaFC.Application.Abstractions;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class PollReminderJob : IPollReminderJob
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<PollReminderJob> _logger;

    public PollReminderJob(AppDbContext db, IPushService push, ILogger<PollReminderJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    // ── Lembrete antes do prazo ───────────────────────────────────────────────

    public async Task ExecuteReminderAsync(Guid pollId, Guid groupId, string triggerType, CancellationToken ct = default)
    {
        var poll = await _db.Polls
            .FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);

        if (poll is null)
        {
            _logger.LogInformation("PollReminderJob ({TriggerType}): votação {PollId} não encontrada — ignorando.", triggerType, pollId);
            return;
        }

        if (poll.Status == "closed")
        {
            _logger.LogInformation("PollReminderJob ({TriggerType}): votação {PollId} já encerrada — ignorando.", triggerType, pollId);
            return;
        }

        var (title, body) = triggerType == "24h"
            ? ("Votação encerra amanhã 🗳️", $"A votação \"{poll.Title}\" fecha em 24h. Vote antes que seja tarde!")
            : ("Votação encerrando! 🗳️",   $"A votação \"{poll.Title}\" fecha em 2h. Última chance de votar.");

        await _push.SendToGroupAsync(
            groupId, title, body,
            data: new Dictionary<string, string>
            {
                ["type"]    = "poll_reminder",
                ["pollId"]  = pollId.ToString(),
                ["groupId"] = groupId.ToString(),
            },
            cancellationToken: ct);

        _logger.LogInformation(
            "PollReminderJob ({TriggerType}): lembrete enviado para grupo {GroupId}, votação \"{Title}\".",
            triggerType, groupId, poll.Title);
    }

    // ── Fechamento automático no prazo ────────────────────────────────────────

    public async Task ExecuteAutoCloseAsync(Guid pollId, CancellationToken ct = default)
    {
        var poll = await _db.Polls
            .FirstOrDefaultAsync(p => p.Id == pollId, ct);

        if (poll is null)
        {
            _logger.LogInformation("PollReminderJob (close): votação {PollId} não encontrada — ignorando.", pollId);
            return;
        }

        if (poll.Status == "closed")
        {
            _logger.LogInformation("PollReminderJob (close): votação {PollId} já encerrada — ignorando.", pollId);
            return;
        }

        // Verificação de segurança: só fechar se o prazo realmente venceu
        if (!poll.HasExpiredDeadline())
        {
            _logger.LogWarning(
                "PollReminderJob (close): votação {PollId} disparou cedo, prazo ainda não venceu — ignorando.", pollId);
            return;
        }

        poll.Close();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PollReminderJob (close): votação \"{Title}\" ({PollId}) fechada automaticamente.", poll.Title, pollId);

        // Notifica o grupo que a votação foi encerrada
        await _push.SendToGroupAsync(
            poll.GroupId,
            title: "Votação encerrada! 🗳️",
            body:  $"A votação \"{poll.Title}\" foi encerrada automaticamente. Confira os resultados.",
            data: new Dictionary<string, string>
            {
                ["type"]    = "poll_closed",
                ["pollId"]  = pollId.ToString(),
                ["groupId"] = poll.GroupId.ToString(),
            },
            cancellationToken: ct);
    }
}
