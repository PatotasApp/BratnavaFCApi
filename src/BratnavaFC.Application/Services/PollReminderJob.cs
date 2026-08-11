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

        // Para eventos, "Talvez" não é resposta definitiva — apenas "Sim" e "Não" excluem o lembrete.
        // Para votações comuns, qualquer voto já é suficiente para excluir.
        var decidedPlayerIds = await (
            from vote in _db.PollVotes
            join option in _db.PollOptions on vote.OptionId equals option.Id
            where vote.PollId == pollId && (!poll.IsEventType() || option.Text != "Talvez")
            select vote.PlayerId
        ).Distinct().ToListAsync(ct);

        var pendingUserIds = await _db.Players
            .Where(p => p.GroupId == groupId && p.UserId != null && !p.IsGuest && !decidedPlayerIds.Contains(p.Id))
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (pendingUserIds.Count == 0)
        {
            _logger.LogInformation(
                "PollReminderJob ({TriggerType}): todos os membros já responderam definitivamente na votação {PollId} — ignorando.",
                triggerType, pollId);
            return;
        }

        var (title, body) = triggerType == "24h"
            ? ("Votação encerra amanhã 🗳️", $"A votação \"{poll.Title}\" fecha em 24h. Vote antes que seja tarde!")
            : ("Votação encerrando! 🗳️",   $"A votação \"{poll.Title}\" fecha em 2h. Última chance de votar.");

        // Polls de evento (Sim/Talvez/Não fixos) → data-only com IDs das opções
        // para que o app exiba botões de ação diretamente na notificação.
        if (poll.IsEventType())
        {
            var options = await _db.PollOptions
                .Where(o => o.PollId == pollId)
                .Select(o => new { o.Id, o.Text })
                .ToListAsync(ct);

            var simId    = options.FirstOrDefault(o => o.Text == "Sim")?.Id;
            var talvezId = options.FirstOrDefault(o => o.Text == "Talvez")?.Id;
            var naoId    = options.FirstOrDefault(o => o.Text == "Não")?.Id;

            if (simId.HasValue && talvezId.HasValue && naoId.HasValue)
            {
                await _push.SendDataOnlyToUsersAsync(
                    pendingUserIds,
                    data: new Dictionary<string, string>
                    {
                        ["type"]          = "poll_reminder",
                        ["pollType"]      = "event",
                        ["pollId"]        = pollId.ToString(),
                        ["groupId"]       = groupId.ToString(),
                        ["optionSimId"]   = simId.Value.ToString(),
                        ["optionTalvezId"]= talvezId.Value.ToString(),
                        ["optionNaoId"]   = naoId.Value.ToString(),
                        ["title"]         = title,
                        ["body"]          = body,
                    },
                    groupId: groupId,
                    cancellationToken: ct);

                _logger.LogInformation(
                    "PollReminderJob ({TriggerType}): lembrete data-only (evento) enviado para {Count} membro(s), votação \"{Title}\".",
                    triggerType, pendingUserIds.Count, poll.Title);
                return;
            }
        }

        // Votações comuns → notificação padrão sem botões
        await _push.SendToUsersAsync(
            pendingUserIds, title, body,
            data: new Dictionary<string, string>
            {
                ["type"]    = "poll_reminder",
                ["pollId"]  = pollId.ToString(),
                ["groupId"] = groupId.ToString(),
            },
            cancellationToken: ct,
            groupId: groupId);

        _logger.LogInformation(
            "PollReminderJob ({TriggerType}): lembrete enviado para {Count} membro(s) que ainda não votaram, votação \"{Title}\".",
            triggerType, pendingUserIds.Count, poll.Title);
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
                ["pollType"] = poll.Type,
                ["pollId"]  = pollId.ToString(),
                ["groupId"] = poll.GroupId.ToString(),
            },
            cancellationToken: ct);
    }
}
