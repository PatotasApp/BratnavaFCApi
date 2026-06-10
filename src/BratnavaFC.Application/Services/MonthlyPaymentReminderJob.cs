using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MonthlyPaymentReminderJob : IMonthlyPaymentReminderJob
{
    // Brasil UTC-3 permanente
    private static readonly TimeSpan BrasilOffset = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<MonthlyPaymentReminderJob> _logger;

    public MonthlyPaymentReminderJob(AppDbContext db, IPushService push, ILogger<MonthlyPaymentReminderJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(BrasilOffset).DateTime);
        var isFirstOfMonth = today.Day == 1;

        // Grupos com PaymentDueDay configurado
        var groupSettings = await _db.GroupSettings
            .AsNoTracking()
            .Where(s => s.PaymentDueDay != null)
            .Select(s => new { s.GroupId, s.PaymentDueDay })
            .ToListAsync(ct);

        if (groupSettings.Count == 0) return;

        foreach (var gs in groupSettings)
        {
            var dueDay = gs.PaymentDueDay!.Value;
            var sendFirstOfMonth = isFirstOfMonth && dueDay > 1;
            var sendDueDay       = today.Day == dueDay;

            if (!sendFirstOfMonth && !sendDueDay) continue;

            // Jogadores com mensalidade pendente no mês atual
            var pendingPayments = await _db.MonthlyPayments
                .AsNoTracking()
                .Where(mp => mp.GroupId == gs.GroupId
                          && mp.Year  == today.Year
                          && mp.Month == today.Month
                          && mp.Status == PaymentStatus.Pending)
                .Select(mp => new { mp.PlayerId, mp.Amount })
                .ToListAsync(ct);

            if (pendingPayments.Count == 0) continue;

            var playerIds = pendingPayments.Select(p => p.PlayerId).Distinct().ToList();

            var userIds = await _db.Players
                .AsNoTracking()
                .Where(p => playerIds.Contains(p.Id) && p.UserId != null && !p.IsGuest)
                .Select(p => p.UserId!.Value)
                .Distinct()
                .ToListAsync(ct);

            if (userIds.Count == 0) continue;

            string title, body;

            if (sendDueDay)
            {
                title = "Hoje é o prazo da mensalidade! 💰";
                body  = $"Hoje, dia {dueDay}, é o último dia para pagar sua mensalidade. Não deixe para depois!";
            }
            else
            {
                title = "Mensalidade em aberto 💰";
                body  = $"Você tem mensalidade pendente. O prazo para pagamento é até o dia {dueDay}.";
            }

            await _push.SendToUsersAsync(
                userIds, title, body,
                data: new Dictionary<string, string>
                {
                    ["type"]    = "monthly_payment_reminder",
                    ["groupId"] = gs.GroupId.ToString(),
                },
                groupId: gs.GroupId);

            _logger.LogInformation(
                "MonthlyPaymentReminderJob: lembrete '{Type}' enviado para {Count} jogador(es) do grupo {GroupId}.",
                sendDueDay ? "deadline" : "first-of-month", userIds.Count, gs.GroupId);
        }
    }
}
