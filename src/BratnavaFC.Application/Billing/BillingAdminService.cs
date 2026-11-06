using System.Text.Json;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
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
    Task<Result<BillingGroupDetailDto>> ResyncPurchaseAsync(Guid purchaseId, Guid actorUserId, CancellationToken ct);
    Task<BillingSettingsDto> GetSettingsAsync(CancellationToken ct);
    Task<Result<BillingSettingsDto>> UpdateSettingsAsync(Guid actorUserId, UpdateBillingSettingsDto dto, CancellationToken ct);
    Task<Result<BillingSettingsDto>> UpsertProductAsync(Guid actorUserId, UpsertBillingProductDto dto, CancellationToken ct);
    Task<BillingProblemsDto> GetProblemsAsync(CancellationToken ct);
    Task<Result<BillingGroupDetailDto>> SimulateAsync(Guid purchaseId, Guid actorUserId, SimulatePurchaseEventDto dto, CancellationToken ct);
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
            rows.Count(r => r.Snap.Status is GroupBillingStatus.Trial or GroupBillingStatus.Active && r.Snap.DaysRemaining <= 7),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Grace),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Expired),
            rows.Count(r => r.Snap.Status == GroupBillingStatus.Lifetime),
            revenue,
            trialEnded == 0 ? 0 : Math.Round((double)converted / trialEnded, 4),
            converted,
            trialEnded,
            purchases.Count(p => p.State == BillingPurchaseState.Refunded && p.RefundedAt >= since),
            problems.FailedEvents.Count + problems.PendingPurchases.Count + problems.UnconsumedPurchases.Count,
            settings.EnforcementEnabled,
            _billing.GetVerifier(BillingStore.Simulated)?.IsAvailable == true,
            [
                new BillingStoreHealthDto("apple", _billing.GetVerifier(BillingStore.Apple)?.IsAvailable == true),
                new BillingStoreHealthDto("google", _billing.GetVerifier(BillingStore.Google)?.IsAvailable == true),
            ]);
    }

    public async Task<IReadOnlyList<BillingGroupListItemDto>> ListGroupsAsync(string? status, string? search, CancellationToken ct)
    {
        IEnumerable<Row> filtered = await LoadAllAsync(ct);

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
            "expiring" => filtered.Where(r => r.Snap.Status is GroupBillingStatus.Trial or GroupBillingStatus.Active && r.Snap.DaysRemaining <= 7),
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

        var row = (await LoadAllAsync(ct, groupId)).Single();

        var purchases = await _db.BillingPurchases.AsNoTracking().Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.PurchasedAt).ToListAsync(ct);
        var adjustments = await _db.BillingAdjustments.AsNoTracking().Where(a => a.GroupId == groupId)
            .OrderByDescending(a => a.CreateDate).Take(100).ToListAsync(ct);
        var refs = purchases.Select(p => p.TransactionId).Concat(purchases.Where(p => p.PurchaseToken != null).Select(p => p.PurchaseToken!)).ToList();
        var events = await _db.StoreEvents.AsNoTracking()
            .Where(e => e.GroupId == groupId || (e.StoreReference != null && refs.Contains(e.StoreReference)))
            .OrderByDescending(e => e.CreateDate).Take(100).ToListAsync(ct);

        var names = await NamesAsync(purchases.Select(p => p.UserId).Concat(adjustments.Select(a => a.ActorUserId)), ct);

        return new BillingGroupDetailDto(
            ToListItem(row),
            billing.TrialStartedAt,
            billing.TrialEndsAt,
            billing.TrialGranted,
            billing.PaidUntil,
            billing.LifetimeReason,
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
        // A auditoria é também o registro dos dias dados: entra na fila de períodos.
        AddAdjustment(new BillingAdjustmentEntity(groupId, actorUserId, BillingAdjustmentType.ExtendDays, dto.Days, dto.Reason, null));
        await _db.SaveChangesAsync(ct);
        await _billing.RecomputeAsync(groupId, ct);

        var after = (await _db.GroupBillings.AsNoTracking().FirstAsync(b => b.GroupId == groupId, ct)).AccessEndsAt;
        var adj = await _db.BillingAdjustments.OrderByDescending(a => a.CreateDate)
            .FirstAsync(a => a.GroupId == groupId && a.Type == BillingAdjustmentType.ExtendDays, ct);
        _db.Entry(adj).Property(nameof(BillingAdjustmentEntity.Details)).CurrentValue =
            $"Vencimento: {before:yyyy-MM-dd HH:mm} → {after:yyyy-MM-dd HH:mm} (UTC)";
        await _db.SaveChangesAsync(ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(groupId, ct))!, $"Patota estendida em {dto.Days} dia(s).");
    }

    public async Task<Result<BillingGroupDetailDto>> SetLifetimeAsync(Guid groupId, Guid actorUserId, SetLifetimeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<BillingGroupDetailDto>.Fail("Informe o motivo.");

        var billing = await _billing.EnsureGroupAsync(groupId, ct);
        if (billing == null) return Result<BillingGroupDetailDto>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        billing.SetLifetime(dto.Enabled, dto.Reason);
        AddAdjustment(new BillingAdjustmentEntity(groupId, actorUserId,
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

        VerifiedStorePurchase tx;
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

        AddAdjustment(new BillingAdjustmentEntity(result.GroupId, actorUserId, BillingAdjustmentType.Reprocess, null,
            dto.Reason, $"{BillingService.StoreName(store)} {tx.ProductId} tx={tx.TransactionId}"));
        await _db.SaveChangesAsync(ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(result.GroupId!.Value, ct))!, "Compra reprocessada.");
    }

    public async Task<Result<BillingGroupDetailDto>> ResyncPurchaseAsync(Guid purchaseId, Guid actorUserId, CancellationToken ct)
    {
        var p = await _db.BillingPurchases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == purchaseId, ct);
        if (p == null) return Result<BillingGroupDetailDto>.Fail("Compra não encontrada.", ResultStatus.NotFound);
        if (p.Store == BillingStore.Simulated) return Result<BillingGroupDetailDto>.Fail("Compra simulada: use as ações de simulação.");

        return await ReprocessAsync(actorUserId, new ReprocessPurchaseDto(
            BillingService.StoreName(p.Store), p.ProductId,
            p.Store == BillingStore.Apple ? p.TransactionId : null,
            p.Store == BillingStore.Google ? p.PurchaseToken : null,
            p.GroupId, "Conferir compra com a loja"), ct);
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

        AddAdjustment(new BillingAdjustmentEntity(null, actorUserId, BillingAdjustmentType.SettingsChanged, null,
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

        AddAdjustment(new BillingAdjustmentEntity(null, actorUserId, BillingAdjustmentType.ProductChanged, null, dto.Reason,
            $"{BillingService.StoreName(store)} {productId} {dto.Months}m R$ {dto.ReferencePrice:0.00} {(dto.IsActive ? "ativo" : "inativo")}"));
        await _db.SaveChangesAsync(ct);

        return Result<BillingSettingsDto>.Ok(await GetSettingsAsync(ct), "Produto salvo.");
    }

    // ── Problemas ───────────────────────────────────────────────────────────────

    public async Task<BillingProblemsDto> GetProblemsAsync(CancellationToken ct)
    {
        var now = _clock();
        var unconsumed = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.Store == BillingStore.Google && p.ConsumedAt == null && p.State == BillingPurchaseState.Confirmed)
            .OrderBy(p => p.PurchasedAt).Take(100).ToListAsync(ct);
        var pending = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.State == BillingPurchaseState.Pending).OrderByDescending(p => p.CreateDate).Take(100).ToListAsync(ct);
        var failed = await _db.StoreEvents.AsNoTracking()
            .Where(e => e.Status == StoreEventStatus.Failed).OrderByDescending(e => e.CreateDate).Take(100).ToListAsync(ct);
        var since = now.AddDays(-90);
        var refunds = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.State == BillingPurchaseState.Refunded && p.RefundedAt >= since)
            .OrderByDescending(p => p.RefundedAt).Take(100).ToListAsync(ct);

        var all = unconsumed.Concat(pending).Concat(refunds).ToList();
        var groupIds = all.Select(p => p.GroupId).Distinct().ToList();
        var groups = await _db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var names = await NamesAsync(all.Select(p => p.UserId), ct);

        return new BillingProblemsDto(
            unconsumed.Select(p => ToDto(p, groups.GetValueOrDefault(p.GroupId), names)).ToList(),
            pending.Select(p => ToDto(p, groups.GetValueOrDefault(p.GroupId), names)).ToList(),
            failed.Select(ToDto).ToList(),
            refunds.Select(p => ToDto(p, groups.GetValueOrDefault(p.GroupId), names)).ToList());
    }

    // ── Simulação (Development) ─────────────────────────────────────────────────

    public async Task<Result<BillingGroupDetailDto>> SimulateAsync(Guid purchaseId, Guid actorUserId, SimulatePurchaseEventDto dto, CancellationToken ct)
    {
        if (_billing.GetVerifier(BillingStore.Simulated) is not { IsAvailable: true })
            return Result<BillingGroupDetailDto>.Fail("Simulação não está disponível neste ambiente.", ResultStatus.Forbidden);

        var purchase = await _db.BillingPurchases.FirstOrDefaultAsync(p => p.Id == purchaseId, ct);
        if (purchase == null || purchase.Store != BillingStore.Simulated)
            return Result<BillingGroupDetailDto>.Fail("Compra simulada não encontrada.", ResultStatus.NotFound);

        var action = (dto.Action ?? "").Trim().ToLowerInvariant();
        switch (action)
        {
            case "refund":
                purchase.Refund(_clock(), "Reembolso simulado");
                break;
            case "unrefund":
                purchase.ReverseRefund();
                break;
            default:
                return Result<BillingGroupDetailDto>.Fail("Ação inválida (refund, unrefund).");
        }

        AddAdjustment(new BillingAdjustmentEntity(purchase.GroupId, actorUserId, BillingAdjustmentType.SimulatedEvent, null,
            $"Simulação: {action}", purchase.TransactionId));
        await _db.SaveChangesAsync(ct);
        await _billing.RecomputeAsync(purchase.GroupId, ct);

        return Result<BillingGroupDetailDto>.Ok((await GetGroupAsync(purchase.GroupId, ct))!, "Simulação aplicada.");
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────

    /// <summary>Grava com a data do relógio do serviço (a fila de períodos usa essa data).</summary>
    private void AddAdjustment(BillingAdjustmentEntity adjustment)
    {
        _db.BillingAdjustments.Add(adjustment);
        _db.Entry(adjustment).Property(nameof(BaseEntity.CreateDate)).CurrentValue = _clock();
    }

    private sealed record Row(GroupBillingEntity Billing, GroupBillingSnapshot Snap, string GroupName, string? CreatorName,
        DateTime GroupCreatedAt, int PurchaseCount, DateTime? LastPurchaseAt);

    private async Task<List<Row>> LoadAllAsync(CancellationToken ct, Guid? onlyGroup = null)
    {
        var groupsQuery = _db.Groups.AsNoTracking();
        if (onlyGroup is { } og) groupsQuery = groupsQuery.Where(g => g.Id == og);
        var groups = await groupsQuery.Select(g => new { g.Id, g.Name, g.CreateDate, g.CreatedByUserId }).ToListAsync(ct);

        var ids = groups.Select(g => g.Id).ToList();
        var billings = await _db.GroupBillings.AsNoTracking().Where(b => ids.Contains(b.GroupId)).ToDictionaryAsync(b => b.GroupId, ct);

        // Patota que ninguém abriu desde o lançamento ainda não tem linha: cria agora.
        foreach (var g in groups.Where(g => !billings.ContainsKey(g.Id)))
            if (await _billing.EnsureGroupAsync(g.Id, ct) is { } created)
                billings[g.Id] = created;

        var settings = await _billing.GetSettingsAsync(ct);
        var now = _clock();

        var stats = await _db.BillingPurchases.AsNoTracking().Where(p => ids.Contains(p.GroupId))
            .GroupBy(p => p.GroupId)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(p => p.PurchasedAt) })
            .ToDictionaryAsync(x => x.Key, ct);
        var creators = await NamesAsync(groups.Select(g => g.CreatedByUserId), ct);

        return groups.Where(g => billings.ContainsKey(g.Id)).Select(g =>
        {
            var b = billings[g.Id];
            stats.TryGetValue(g.Id, out var st);
            return new Row(b, BillingStatusCalculator.Calculate(b, settings.GraceDays, settings.EnforcementEnabled, now),
                g.Name, g.CreatedByUserId is { } c ? creators.GetValueOrDefault(c) : null, g.CreateDate,
                st?.Count ?? 0, st?.Last);
        }).ToList();
    }

    private static BillingGroupListItemDto ToListItem(Row r) => new(
        r.Billing.GroupId, r.GroupName, r.CreatorName, BillingService.StatusName(r.Snap.Status), r.Snap.AccessEndsAt,
        r.Billing.IsLifetime ? null : r.Snap.AccessEndsOn.ToString("yyyy-MM-dd"),
        r.Billing.IsLifetime ? null : r.Snap.DaysRemaining,
        r.Billing.IsLifetime, r.PurchaseCount, r.LastPurchaseAt, r.GroupCreatedAt);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return [];
        return await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);
    }

    private static AdminPurchaseDto ToDto(BillingPurchaseEntity p, string? groupName, IReadOnlyDictionary<Guid, string> names) => new(
        p.Id, p.GroupId, groupName, BillingService.StoreName(p.Store), p.ProductId, p.TransactionId, p.PurchaseToken, p.Months,
        p.PurchasedAt, p.PeriodStart, p.PeriodEnd, p.State.ToString().ToLowerInvariant(),
        p.PriceMicros is { } m ? m / 1_000_000m : null, p.Currency, p.Environment,
        p.UserId is { } u ? names.GetValueOrDefault(u) : null, p.ConsumedAt, p.RefundedAt, p.RefundReason);

    private static BillingAdjustmentDto ToDto(BillingAdjustmentEntity a, IReadOnlyDictionary<Guid, string> names) => new(
        a.Id, a.GroupId, a.Type.ToString(), a.Days, a.Reason, a.Details,
        a.ActorUserId is { } u ? names.GetValueOrDefault(u) : null, a.CreateDate);

    private static StoreEventDto ToDto(StoreEventEntity e) => new(
        e.Id, BillingService.StoreName(e.Store), e.ExternalId, e.NotificationType, e.Subtype, e.Status.ToString(), e.Error,
        e.GroupId, e.StoreReference, e.Attempts, e.CreateDate, e.ProcessedAt);

    private static string Snapshot(BillingSettingsEntity s) => JsonSerializer.Serialize(new
    {
        s.TrialDays, s.GraceDays, s.EnforcementEnabled, s.RemindersEnabled, s.ReminderDays,
    });
}
