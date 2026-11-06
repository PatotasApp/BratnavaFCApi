using System.Text.Json;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities.Billing;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Billing;

public interface IBillingAdminService
{
    Task<BillingDashboardDto> GetDashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<BillingGroupListItemDto>> ListGroupsAsync(string? status, string? search, CancellationToken ct);
    Task<BillingGroupDetailDto?> GetGroupAsync(Guid groupId, CancellationToken ct);
    Task<Result<BillingGroupDetailDto>> ExtendAsync(Guid groupId, Guid actorUserId, ExtendBillingDto dto, CancellationToken ct);
    Task<Result<BillingGroupDetailDto>> SetLifetimeAsync(Guid groupId, Guid actorUserId, SetLifetimeDto dto, CancellationToken ct);
    Task<Result<BillingGroupDetailDto>> ReprocessAsync(Guid actorUserId, ReprocessPurchaseDto dto, CancellationToken ct);
    Task<Result<BillingGroupDetailDto>> ResyncSubscriptionAsync(Guid subscriptionId, Guid actorUserId, CancellationToken ct);
    Task<BillingSettingsDto> GetSettingsAsync(CancellationToken ct);
    Task<Result<BillingSettingsDto>> UpdateSettingsAsync(Guid actorUserId, UpdateBillingSettingsDto dto, CancellationToken ct);
    Task<Result<BillingSettingsDto>> UpsertProductAsync(Guid actorUserId, UpsertBillingProductDto dto, CancellationToken ct);
    Task<BillingProblemsDto> GetProblemsAsync(CancellationToken ct);
    Task<Result<BillingGroupDetailDto>> SimulateAsync(Guid subscriptionId, Guid actorUserId, SimulateSubscriptionEventDto dto, CancellationToken ct);
}

/// <summary>Administração da cobrança, só para o GodMode (área /app/admin/godmode do site).</summary>
public sealed class BillingAdminService : IBillingAdminService
{
    private readonly AppDbContext _db;
    private readonly IBillingService _billing;
    private readonly Func<DateTime> _clock;

    public BillingAdminService(AppDbContext db, IBillingService billing) : this(db, billing, () => DateTime.UtcNow) { }

    internal BillingAdminService(AppDbContext db, IBillingService billing, Func<DateTime> clock)
    {
        _db = db;
        _billing = billing;
        _clock = clock;
    }

    // ── Dashboard e lista ───────────────────────────────────────────────────────

    public async Task<BillingDashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var rows = await LoadAllAsync(ct);
        var settings = await _billing.GetSettingsAsync(ct);
        var now = _clock();
        var since = now.AddDays(-30);

        var purchases = await _db.BillingPurchases.AsNoTracking().ToListAsync(ct);
        var revenue = purchases
            .Where(p => p.State == BillingPurchaseState.Confirmed && p.PriceMicros != null && p.Environment == "Production")
            .GroupBy(p => p.Currency ?? "BRL")
            .Select(g => new BillingRevenueDto(
                g.Key,
                g.Where(p => p.PurchasedAt >= since).Sum(p => p.PriceMicros!.Value) / 1_000_000m,
                g.Sum(p => p.PriceMicros!.Value) / 1_000_000m,
                g.Count(p => p.PurchasedAt >= since)))
            .ToList();

        var paidGroups = purchases.Where(p => p.State != BillingPurchaseState.Pending && p.Environment != "Simulated")
            .Select(p => p.GroupId).ToHashSet();
        var nonLifetime = rows.Where(r => !r.Billing.IsLifetime).ToList();
        var trialEnded = nonLifetime.Count(r => r.Billing.TrialEndsAt <= now || paidGroups.Contains(r.Billing.GroupId));
        var converted = nonLifetime.Count(r => paidGroups.Contains(r.Billing.GroupId));

        var problems = await GetProblemsAsync(ct);

        return new BillingDashboardDto(
            rows.Count,
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Trial),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Active),
            rows.Count(r => r.Snap.Status is GroupBillingStatus.Trial or GroupBillingStatus.Active && r.Snap.DaysRemaining <= 7 && !r.AutoRenew),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Grace),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Expired),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Lifetime),
            revenue,
            trialEnded == 0 ? 0 : Math.Round((double)converted / trialEnded, 4),
            converted,
            trialEnded,
            purchases.Count(p => p.State == BillingPurchaseState.Refunded && p.RefundedAt >= since),
            problems.FailedEvents.Count + problems.PendingPurchases.Count + problems.UnacknowledgedSubscriptions.Count,
            settings.EnforcementEnabled,
            _billing.GetVerifier(BillingStore.Simulated)?.IsAvailable == true,
            [
                new BillingStoreHealthDto("apple", _billing.GetVerifier(BillingStore.Apple)?.IsAvailable == true),
                new BillingStoreHealthDto("google", _billing.GetVerifier(BillingStore.Google)?.IsAvailable == true),
            ]);
    }

    public async Task<IReadOnlyList<BillingGroupListItemDto>> ListGroupsAsync(string? status, string? search, CancellationToken ct)
    {
        var rows = await LoadAllAsync(ct);
        var now = _clock();
        IEnumerable<Row> filtered = rows;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            filtered = filtered.Where(r => r.GroupName.Contains(s, StringComparison.OrdinalIgnoreCase)
                                        || (r.CreatorName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                                        || r.Billing.GroupId.ToString().StartsWith(s, StringComparison.OrdinalIgnoreCase));
        }

        filtered = (status ?? "").Trim().ToLowerInvariant() switch
        {
            "trial" => filtered.Where(r => r.Snap.Status == GroupBillingStatus.Trial),
            "active" => filtered.Where(r => r.Snap.Status == GroupBillingStatus.Active),
            "grace" => filtered.Where(r => r.Snap.Status == GroupBillingStatus.Grace),
            "expired" => filtered.Where(r => r.Snap.Status == GroupBillingStatus.Expired),
            "lifetime" => filtered.Where(r => r.Snap.Status == GroupBillingStatus.Lifetime),
            "expiring" => filtered.Where(r => r.Snap.Status is GroupBillingStatus.Trial or GroupBillingStatus.Active && r.Snap.DaysRemaining <= 7 && !r.AutoRenew),
            _ => filtered,
        };

        return filtered
            .OrderBy(r => r.Snap.Status == GroupBillingStatus.Lifetime)
            .ThenBy(r => r.Snap.AccessEndsAt)
            .Select(ToListItem)
            .ToList();
    }

    public async Task<BillingGroupDetailDto?> GetGroupAsync(Guid groupId, CancellationToken ct)
    {
        var billing = await _billing.EnsureGroupAsync(groupId, ct);
        if (billing == null) return null;

        var rows = await LoadAllAsync(ct, groupId);
        var row = rows.Single();

        var subs = await _db.StoreSubscriptions.AsNoTracking().Where(s => s.GroupId == groupId)
            .OrderByDescending(s => s.CreateDate).ToListAsync(ct);
        var purchases = await _db.BillingPurchases.AsNoTracking().Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.PurchasedAt).ToListAsync(ct);
        var adjustments = await _db.BillingAdjustments.AsNoTracking().Where(a => a.GroupId == groupId)
            .OrderByDescending(a => a.CreateDate).Take(100).ToListAsync(ct);
        var subIds = subs.Select(s => s.StoreSubscriptionId).ToList();
        var events = await _db.StoreEvents.AsNoTracking()
            .Where(e => e.GroupId == groupId || (e.StoreSubscriptionId != null && subIds.Contains(e.StoreSubscriptionId)))
            .OrderByDescending(e => e.CreateDate).Take(100).ToListAsync(ct);

        var names = await NamesAsync(
            subs.Select(s => s.PayerUserId).Concat(purchases.Select(p => p.UserId)).Concat(adjustments.Select(a => a.ActorUserId)), ct);

        return new BillingGroupDetailDto(
            ToListItem(row),
            billing.TrialStartedAt,
            billing.TrialEndsAt,
            billing.TrialGranted,
            billing.PaidUntil,
            billing.CourtesyUntil,
            billing.LifetimeReason,
            subs.Select(s => ToDto(s, names)).ToList(),
            purchases.Select(p => ToDto(p, row.GroupName, names)).ToList(),
            adjustments.Select(a => ToDto(a, names)).ToList(),
            events.Select(ToDto).ToList());
    }

    // ── Ações ───────────────────────────────────────────────────────────────────

    public async Task<Result<BillingGroupDetailDto>> ExtendAsync(Guid groupId, Guid actorUserId, ExtendBillingDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<BillingGroupDetailDto>.Fail("Informe o motivo.");
        if (dto.Days is < 1 or > 3650) return Result<BillingGroupDetailDto>.Fail("Informe de 1 a 3650 dias.");

        var billing = await _billing.EnsureGroupAsync(groupId, ct);
        if (billing == null) return Result<BillingGroupDetailDto>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        var before = billing.AccessEndsAt;
        billing.Extend(dto.Days, _clock());
        _db.BillingAdjustments.Add(new BillingAdjustmentEntity(groupId, actorUserId, BillingAdjustmentType.ExtendDays, dto.Days,
            dto.Reason, $"Vencimento: {before:yyyy-MM-dd HH:mm} → {billing.AccessEndsAt:yyyy-MM-dd HH:mm} (UTC)"));
        await _db.SaveChangesAsync(ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(groupId, ct))!, $"Patota estendida em {dto.Days} dia(s).");
    }

    public async Task<Result<BillingGroupDetailDto>> SetLifetimeAsync(Guid groupId, Guid actorUserId, SetLifetimeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<BillingGroupDetailDto>.Fail("Informe o motivo.");

        var billing = await _billing.EnsureGroupAsync(groupId, ct);
        if (billing == null) return Result<BillingGroupDetailDto>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        billing.SetLifetime(dto.Enabled, dto.Reason);
        _db.BillingAdjustments.Add(new BillingAdjustmentEntity(groupId, actorUserId,
            dto.Enabled ? BillingAdjustmentType.SetLifetime : BillingAdjustmentType.RemoveLifetime, null, dto.Reason, null));
        await _db.SaveChangesAsync(ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(groupId, ct))!,
            dto.Enabled ? "Patota liberada (vitalício)." : "Plano vitalício removido.");
    }

    public async Task<Result<BillingGroupDetailDto>> ReprocessAsync(Guid actorUserId, ReprocessPurchaseDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<BillingGroupDetailDto>.Fail("Informe o motivo.");
        if (!BillingService.TryParseStore(dto.Store, out var store)) return Result<BillingGroupDetailDto>.Fail("Loja inválida.");
        var verifier = _billing.GetVerifier(store);
        if (verifier is not { IsAvailable: true }) return Result<BillingGroupDetailDto>.Fail("Loja não configurada no servidor.");

        VerifiedStoreTransaction tx;
        try
        {
            tx = await verifier.VerifyAsync(new StorePurchaseProof(dto.ProductId?.Trim() ?? "", dto.TransactionId, dto.PurchaseToken, dto.GroupId), ct);
        }
        catch (StoreVerificationException ex)
        {
            return Result<BillingGroupDetailDto>.Fail($"A loja recusou: {ex.Message}");
        }

        var result = await _billing.ApplyVerifiedAsync(tx, dto.GroupId, null, ct);
        if (!result.Success) return Result<BillingGroupDetailDto>.Fail(result.Error!);

        _db.BillingAdjustments.Add(new BillingAdjustmentEntity(result.GroupId, actorUserId, BillingAdjustmentType.Reprocess, null,
            dto.Reason, $"{BillingService.StoreName(store)} {tx.ProductId} tx={tx.TransactionId} expira={tx.ExpiresAt:yyyy-MM-dd}"));
        await _db.SaveChangesAsync(ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(result.GroupId!.Value, ct))!, "Compra reprocessada.");
    }

    public async Task<Result<BillingGroupDetailDto>> ResyncSubscriptionAsync(Guid subscriptionId, Guid actorUserId, CancellationToken ct)
    {
        var sub = await _db.StoreSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub == null) return Result<BillingGroupDetailDto>.Fail("Assinatura não encontrada.", ResultStatus.NotFound);
        if (sub.Store == BillingStore.Simulated) return Result<BillingGroupDetailDto>.Fail("Assinatura simulada: use as ações de simulação.");

        // Apple: consulta pela última transação; Google: pelo purchaseToken.
        var lastTx = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.SubscriptionId == sub.Id).OrderByDescending(p => p.PurchasedAt)
            .Select(p => p.TransactionId).FirstOrDefaultAsync(ct);

        return await ReprocessAsync(actorUserId, new ReprocessPurchaseDto(
            BillingService.StoreName(sub.Store), sub.ProductId,
            sub.Store == BillingStore.Apple ? lastTx ?? sub.StoreSubscriptionId : null,
            sub.Store == BillingStore.Google ? sub.StoreSubscriptionId : null,
            sub.GroupId, "Sincronizar assinatura com a loja"), ct);
    }

    // ── Configuração ────────────────────────────────────────────────────────────

    public async Task<BillingSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await _billing.GetSettingsAsync(ct);
        var products = await _db.BillingProducts.AsNoTracking()
            .OrderBy(p => p.Store).ThenBy(p => p.Months).ThenByDescending(p => p.IsActive).ToListAsync(ct);
        return new BillingSettingsDto(
            s.TrialDays, s.GraceDays, s.EnforcementEnabled, s.RemindersEnabled, s.ReminderDays, s.LaunchedAt, s.UpdatedAt,
            products.Select(p => new AdminProductDto(p.Id, BillingService.StoreName(p.Store), p.ProductId, p.Months,
                p.ReferencePriceCents / 100m, p.IsActive)).ToList(),
            _billing.GetVerifier(BillingStore.Simulated)?.IsAvailable == true);
    }

    public async Task<Result<BillingSettingsDto>> UpdateSettingsAsync(Guid actorUserId, UpdateBillingSettingsDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<BillingSettingsDto>.Fail("Informe o motivo.");

        var s = await _billing.GetSettingsAsync(ct);
        var before = Snapshot(s);
        try
        {
            s.Update(dto.TrialDays, dto.GraceDays, dto.EnforcementEnabled, dto.RemindersEnabled, dto.ReminderDays, actorUserId);
        }
        catch (ArgumentException ex)
        {
            return Result<BillingSettingsDto>.Fail(ex.Message);
        }

        _db.BillingAdjustments.Add(new BillingAdjustmentEntity(null, actorUserId, BillingAdjustmentType.SettingsChanged, null,
            dto.Reason, $"{before} → {Snapshot(s)}"));
        await _db.SaveChangesAsync(ct);
        _billing.InvalidateSettingsCache();

        return Result<BillingSettingsDto>.Ok(await GetSettingsAsync(ct), "Configurações salvas.");
    }

    public async Task<Result<BillingSettingsDto>> UpsertProductAsync(Guid actorUserId, UpsertBillingProductDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<BillingSettingsDto>.Fail("Informe o motivo.");
        if (!BillingService.TryParseStore(dto.Store, out var store)) return Result<BillingSettingsDto>.Fail("Loja inválida.");
        if (string.IsNullOrWhiteSpace(dto.ProductId)) return Result<BillingSettingsDto>.Fail("Informe o ID do produto.");
        if (dto.Months is not (1 or 3 or 6 or 12)) return Result<BillingSettingsDto>.Fail("Duração deve ser 1, 3, 6 ou 12 meses.");
        if (dto.ReferencePrice < 0) return Result<BillingSettingsDto>.Fail("Preço de referência inválido.");

        var cents = (int)Math.Round(dto.ReferencePrice * 100m, MidpointRounding.AwayFromZero);
        var productId = dto.ProductId.Trim();

        var product = await _db.BillingProducts.FirstOrDefaultAsync(p => p.Store == store && p.ProductId == productId, ct);
        if (product != null && product.Months != dto.Months)
            return Result<BillingSettingsDto>.Fail("Este produto já existe com outra duração. Crie outro ID na loja.");

        // Só um ativo por loja e duração: ativar este desativa o anterior.
        if (dto.IsActive)
        {
            var others = await _db.BillingProducts
                .Where(p => p.Store == store && p.Months == dto.Months && p.IsActive && p.ProductId != productId)
                .ToListAsync(ct);
            foreach (var o in others) o.Deactivate();
            if (others.Count > 0) await _db.SaveChangesAsync(ct);
        }

        if (product == null)
            _db.BillingProducts.Add(new BillingProductEntity(store, productId, dto.Months, cents, dto.IsActive));
        else
            product.Update(cents, dto.IsActive);

        _db.BillingAdjustments.Add(new BillingAdjustmentEntity(null, actorUserId, BillingAdjustmentType.ProductChanged, null, dto.Reason,
            $"{BillingService.StoreName(store)} {productId} {dto.Months}m R$ {dto.ReferencePrice:0.00} {(dto.IsActive ? "ativo" : "inativo")}"));
        await _db.SaveChangesAsync(ct);

        return Result<BillingSettingsDto>.Ok(await GetSettingsAsync(ct), "Produto salvo.");
    }

    // ── Problemas ───────────────────────────────────────────────────────────────

    public async Task<BillingProblemsDto> GetProblemsAsync(CancellationToken ct)
    {
        var now = _clock();
        var unack = await _db.StoreSubscriptions.AsNoTracking()
            .Where(s => s.Store == BillingStore.Google && s.AcknowledgedAt == null
                        && s.State != StoreSubscriptionState.Pending && s.State != StoreSubscriptionState.Expired)
            .ToListAsync(ct);
        var pending = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.State == BillingPurchaseState.Pending).OrderByDescending(p => p.CreateDate).Take(100).ToListAsync(ct);
        var failed = await _db.StoreEvents.AsNoTracking()
            .Where(e => e.Status == StoreEventStatus.Failed).OrderByDescending(e => e.CreateDate).Take(100).ToListAsync(ct);
        var since = now.AddDays(-90);
        var refunds = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.State == BillingPurchaseState.Refunded && p.RefundedAt >= since)
            .OrderByDescending(p => p.RefundedAt).Take(100).ToListAsync(ct);

        var groupIds = unack.Select(s => s.GroupId).Concat(pending.Select(p => p.GroupId)).Concat(refunds.Select(p => p.GroupId)).Distinct().ToList();
        var groups = await _db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var names = await NamesAsync(unack.Select(s => s.PayerUserId).Concat(pending.Select(p => p.UserId)).Concat(refunds.Select(p => p.UserId)), ct);

        return new BillingProblemsDto(
            unack.Select(s => new AdminSubscriptionWithGroupDto(ToDto(s, names), s.GroupId, groups.GetValueOrDefault(s.GroupId))).ToList(),
            pending.Select(p => ToDto(p, groups.GetValueOrDefault(p.GroupId), names)).ToList(),
            failed.Select(ToDto).ToList(),
            refunds.Select(p => ToDto(p, groups.GetValueOrDefault(p.GroupId), names)).ToList());
    }

    // ── Simulação (Development) ─────────────────────────────────────────────────

    public async Task<Result<BillingGroupDetailDto>> SimulateAsync(Guid subscriptionId, Guid actorUserId, SimulateSubscriptionEventDto dto, CancellationToken ct)
    {
        if (_billing.GetVerifier(BillingStore.Simulated) is not { IsAvailable: true })
            return Result<BillingGroupDetailDto>.Fail("Simulação não está disponível neste ambiente.", ResultStatus.Forbidden);

        var sub = await _db.StoreSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub == null || sub.Store != BillingStore.Simulated)
            return Result<BillingGroupDetailDto>.Fail("Assinatura simulada não encontrada.", ResultStatus.NotFound);

        var product = await _db.BillingProducts.AsNoTracking()
            .FirstAsync(p => p.Store == BillingStore.Simulated && p.ProductId == sub.ProductId, ct);
        var now = _clock();
        var action = (dto.Action ?? "").Trim().ToLowerInvariant();

        switch (action)
        {
            case "renew":
            {
                var from = sub.ExpiresAt is { } e && e > now ? e : now;
                var tx = new VerifiedStoreTransaction(BillingStore.Simulated, sub.StoreSubscriptionId, $"sim-tx-{Guid.NewGuid():N}",
                    sub.ProductId, from, from.AddMonths(product.Months), StoreSubscriptionState.Active, true, false, null, null,
                    sub.GroupId, product.ReferencePriceCents * 10_000L, "BRL", "Simulated", false);
                var r = await _billing.ApplyVerifiedAsync(tx, sub.GroupId, sub.PayerUserId, ct);
                if (!r.Success) return Result<BillingGroupDetailDto>.Fail(r.Error!);
                break;
            }
            case "refund":
            {
                var last = await _db.BillingPurchases
                    .Where(p => p.SubscriptionId == sub.Id && p.State == BillingPurchaseState.Confirmed)
                    .OrderByDescending(p => p.ExpiresAt).FirstOrDefaultAsync(ct);
                if (last == null) return Result<BillingGroupDetailDto>.Fail("Nada para reembolsar.");
                await _billing.RefundTransactionAsync(BillingStore.Simulated, last.TransactionId, "Reembolso simulado", now, ct);
                sub.Sync(sub.ProductId, StoreSubscriptionState.Revoked, false, sub.ExpiresAt);
                break;
            }
            case "cancel":
                sub.Sync(sub.ProductId, sub.State, false, sub.ExpiresAt);
                break;
            case "resume":
                sub.Sync(sub.ProductId, StoreSubscriptionState.Active, true, sub.ExpiresAt);
                break;
            case "expire":
            {
                // Faz a assinatura vencer agora (como se a loja não tivesse conseguido cobrar).
                var live = await _db.BillingPurchases
                    .Where(p => p.SubscriptionId == sub.Id && p.State == BillingPurchaseState.Confirmed && p.ExpiresAt > now)
                    .ToListAsync(ct);
                foreach (var p in live) p.Confirm(now);
                sub.Sync(sub.ProductId, StoreSubscriptionState.Expired, false, now);
                await _db.SaveChangesAsync(ct);
                await _billing.RecomputePaidUntilAsync(sub.GroupId, ct);
                break;
            }
            default:
                return Result<BillingGroupDetailDto>.Fail("Ação inválida (renew, refund, cancel, resume, expire).");
        }

        _db.BillingAdjustments.Add(new BillingAdjustmentEntity(sub.GroupId, actorUserId, BillingAdjustmentType.SimulatedEvent, null,
            $"Simulação: {action}", sub.StoreSubscriptionId));
        await _db.SaveChangesAsync(ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(sub.GroupId, ct))!, "Simulação aplicada.");
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────

    private sealed record Row(GroupBillingEntity Billing, GroupBillingSnapshot Snap, string GroupName, string? CreatorName,
        DateTime GroupCreatedAt, BillingStore? Store, bool AutoRenew, int PurchaseCount);

    private async Task<List<Row>> LoadAllAsync(CancellationToken ct, Guid? onlyGroup = null)
    {
        var groupsQuery = _db.Groups.AsNoTracking();
        if (onlyGroup is { } og) groupsQuery = groupsQuery.Where(g => g.Id == og);
        var groups = await groupsQuery
            .Select(g => new { g.Id, g.Name, g.CreateDate, g.CreatedByUserId })
            .ToListAsync(ct);

        var ids = groups.Select(g => g.Id).ToList();
        var billings = await _db.GroupBillings.AsNoTracking().Where(b => ids.Contains(b.GroupId)).ToDictionaryAsync(b => b.GroupId, ct);

        // Patota que ninguém abriu desde o lançamento ainda não tem linha: cria agora.
        foreach (var g in groups.Where(g => !billings.ContainsKey(g.Id)))
            if (await _billing.EnsureGroupAsync(g.Id, ct) is { } created)
                billings[g.Id] = created;

        var settings = await _billing.GetSettingsAsync(ct);
        var now = _clock();

        var subs = await _db.StoreSubscriptions.AsNoTracking().Where(s => ids.Contains(s.GroupId)).ToListAsync(ct);
        var purchaseCounts = await _db.BillingPurchases.AsNoTracking().Where(p => ids.Contains(p.GroupId))
            .GroupBy(p => p.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var creators = await NamesAsync(groups.Select(g => g.CreatedByUserId), ct);

        return groups.Where(g => billings.ContainsKey(g.Id)).Select(g =>
        {
            var b = billings[g.Id];
            var live = subs.Where(s => s.GroupId == g.Id && BillingService.IsLive(s, now))
                .OrderByDescending(s => s.ExpiresAt).FirstOrDefault();
            return new Row(b, BillingStatusCalculator.Calculate(b, settings.GraceDays, settings.EnforcementEnabled, now),
                g.Name, g.CreatedByUserId is { } c ? creators.GetValueOrDefault(c) : null, g.CreateDate,
                live?.Store, live?.AutoRenewEnabled ?? false, purchaseCounts.GetValueOrDefault(g.Id));
        }).ToList();
    }

    private static BillingGroupListItemDto ToListItem(Row r) => new(
        r.Billing.GroupId, r.GroupName, r.CreatorName, BillingService.StatusName(r.Snap.Status), r.Snap.AccessEndsAt,
        r.Billing.IsLifetime ? null : r.Snap.AccessEndsOn.ToString("yyyy-MM-dd"),
        r.Billing.IsLifetime ? null : r.Snap.DaysRemaining,
        r.Billing.IsLifetime, r.Store is { } s ? BillingService.StoreName(s) : null, r.AutoRenew, r.PurchaseCount, r.GroupCreatedAt);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return [];
        return await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);
    }

    private static AdminSubscriptionDto ToDto(StoreSubscriptionEntity s, IReadOnlyDictionary<Guid, string> names) => new(
        s.Id, BillingService.StoreName(s.Store), s.StoreSubscriptionId, s.ProductId, s.State.ToString(), s.AutoRenewEnabled,
        s.ExpiresAt, s.Environment, s.PayerUserId is { } p ? names.GetValueOrDefault(p) : null, s.AcknowledgedAt, s.LastSyncedAt);

    private static AdminPurchaseDto ToDto(BillingPurchaseEntity p, string? groupName, IReadOnlyDictionary<Guid, string> names) => new(
        p.Id, p.GroupId, groupName, BillingService.StoreName(p.Store), p.ProductId, p.TransactionId, p.Months, p.PurchasedAt,
        p.ExpiresAt, p.State.ToString().ToLowerInvariant(), p.PriceMicros is { } m ? m / 1_000_000m : null, p.Currency,
        p.Environment, p.UserId is { } u ? names.GetValueOrDefault(u) : null, p.RefundedAt, p.RefundReason);

    private static BillingAdjustmentDto ToDto(BillingAdjustmentEntity a, IReadOnlyDictionary<Guid, string> names) => new(
        a.Id, a.GroupId, a.Type.ToString(), a.Days, a.Reason, a.Details,
        a.ActorUserId is { } u ? names.GetValueOrDefault(u) : null, a.CreateDate);

    private static StoreEventDto ToDto(StoreEventEntity e) => new(
        e.Id, BillingService.StoreName(e.Store), e.ExternalId, e.NotificationType, e.Subtype, e.Status.ToString(), e.Error,
        e.GroupId, e.StoreSubscriptionId, e.Attempts, e.CreateDate, e.ProcessedAt);

    private static string Snapshot(BillingSettingsEntity s) => JsonSerializer.Serialize(new
    {
        s.TrialDays, s.GraceDays, s.EnforcementEnabled, s.RemindersEnabled, s.ReminderDays,
    });
}
