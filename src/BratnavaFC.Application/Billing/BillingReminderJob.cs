using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities.Billing;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Billing;

public interface IBillingReminderJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Job diário da cobrança:
/// <list type="bullet">
/// <item>lembrete por push para admins e financeiros N dias antes de vencer (padrão 7, 3, 1 e
/// no dia), só quando nada vai renovar sozinho e os lembretes estão ligados no painel;</item>
/// <item>nova tentativa de confirmar (acknowledge) compras do Google que ficaram sem
/// confirmação — o Google reembolsa sozinho depois de 3 dias.</item>
/// </list>
/// </summary>
public sealed class BillingReminderJob : IBillingReminderJob
{
    private readonly AppDbContext _db;
    private readonly IBillingService _billing;
    private readonly IPushService _push;
    private readonly ILogger<BillingReminderJob> _logger;
    private readonly Func<DateTime> _clock;

    public BillingReminderJob(AppDbContext db, IBillingService billing, IPushService push, ILogger<BillingReminderJob> logger)
        : this(db, billing, push, logger, () => DateTime.UtcNow) { }

    internal BillingReminderJob(AppDbContext db, IBillingService billing, IPushService push, ILogger<BillingReminderJob> logger, Func<DateTime> clock)
    {
        _db = db;
        _billing = billing;
        _push = push;
        _logger = logger;
        _clock = clock;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await RetryAcknowledgementsAsync(ct);
        await SendRemindersAsync(ct);
    }

    public async Task<int> SendRemindersAsync(CancellationToken ct)
    {
        var settings = await _billing.GetSettingsAsync(ct);
        if (!settings.RemindersEnabled) return 0;

        var days = settings.GetReminderDays();
        if (days.Count == 0) return 0;

        var now = _clock();
        var today = BillingStatusCalculator.BrasiliaDate(now);
        var maxDays = days.Max();

        // Garante a linha das patotas que ainda não tinham (o teste começa a contar aqui).
        var missing = await _db.Groups.AsNoTracking()
            .Where(g => !_db.GroupBillings.Any(b => b.GroupId == g.Id))
            .Select(g => g.Id).ToListAsync(ct);
        foreach (var id in missing) await _billing.EnsureGroupAsync(id, ct);

        var horizon = now.AddDays(maxDays + 2);
        var candidates = await _db.GroupBillings
            .Where(b => !b.IsLifetime)
            .ToListAsync(ct);

        var liveRenewing = (await _db.StoreSubscriptions.AsNoTracking()
                .Where(s => s.AutoRenewEnabled).ToListAsync(ct))
            .Where(s => BillingService.IsLive(s, now))
            .Select(s => s.GroupId).ToHashSet();

        var sent = 0;
        foreach (var billing in candidates)
        {
            if (billing.AccessEndsAt > horizon) continue;
            if (liveRenewing.Contains(billing.GroupId)) continue;

            var snap = BillingStatusCalculator.Calculate(billing, settings.GraceDays, settings.EnforcementEnabled, now);
            if (!days.Contains(snap.DaysRemaining)) continue;

            var key = $"{snap.AccessEndsOn:yyyy-MM-dd}:{snap.DaysRemaining}";
            if (billing.LastReminderKey == key) continue;

            var groupName = await _db.Groups.AsNoTracking().Where(g => g.Id == billing.GroupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
            if (groupName == null) continue;

            var userIds = await _db.GroupAdmins.AsNoTracking().Where(a => a.GroupId == billing.GroupId).Select(a => a.UserId)
                .Union(_db.GroupFinanceiros.AsNoTracking().Where(f => f.GroupId == billing.GroupId).Select(f => f.UserId))
                .Distinct().ToListAsync(ct);
            if (userIds.Count == 0) continue;

            var (title, body) = Message(groupName, snap.DaysRemaining, snap.AccessEndsOn, billing.PaidUntil == null);

            await _push.SendToUsersAsync(userIds, title, body,
                data: new Dictionary<string, string>
                {
                    ["type"] = "billing_reminder",
                    ["groupId"] = billing.GroupId.ToString(),
                },
                groupId: billing.GroupId,
                cancellationToken: ct);

            billing.MarkReminded(key);
            await _db.SaveChangesAsync(ct);
            sent++;
        }

        if (sent > 0)
            _logger.LogInformation("[Billing] {Count} lembrete(s) de vencimento enviados ({Today}).", sent, today);
        return sent;
    }

    /// <summary>O push já prefixa o nome da patota no título (groupId), então ele não entra aqui.</summary>
    public static (string Title, string Body) Message(string groupName, int daysRemaining, DateOnly endsOn, bool isTrial)
    {
        var what = isTrial ? "O teste grátis" : "O plano";
        var date = endsOn.ToString("dd/MM");
        return daysRemaining switch
        {
            0 => ("Plano vence hoje", $"{what} da patota vence hoje ({date}). Abra Minha patota → Plano da patota para continuar usando tudo."),
            1 => ("Plano vence amanhã", $"{what} da patota vence amanhã ({date}). Pague em Minha patota → Plano da patota."),
            _ => ($"Plano vence em {daysRemaining} dias", $"{what} da patota vence em {date}. Pague em Minha patota → Plano da patota."),
        };
    }

    private async Task RetryAcknowledgementsAsync(CancellationToken ct)
    {
        var verifier = _billing.GetVerifier(BillingStore.Google);
        if (verifier is not { IsAvailable: true }) return;

        var pending = await _db.StoreSubscriptions.AsNoTracking()
            .Where(s => s.Store == BillingStore.Google && s.AcknowledgedAt == null
                        && s.State != StoreSubscriptionState.Pending && s.State != StoreSubscriptionState.Expired)
            .ToListAsync(ct);

        foreach (var s in pending)
        {
            try
            {
                var tx = await verifier.VerifyAsync(new StorePurchaseProof(s.ProductId, null, s.StoreSubscriptionId, s.GroupId), ct);
                await _billing.ApplyVerifiedAsync(tx, s.GroupId, null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Billing] Nova tentativa de confirmar a compra {Token} falhou.", s.StoreSubscriptionId);
            }
        }
    }
}
