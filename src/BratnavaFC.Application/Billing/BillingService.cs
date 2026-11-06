using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities.Billing;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BratnavaFC.Application.Billing;

public interface IBillingService
{
    Task<BillingSettingsEntity> GetSettingsAsync(CancellationToken ct);

    /// <summary>Parâmetros com cache curto, para o filtro de bloqueio não ir ao banco a cada escrita.</summary>
    Task<(bool EnforcementEnabled, int GraceDays)> GetGateSettingsAsync(CancellationToken ct);

    void InvalidateSettingsCache();

    /// <summary>Cria a linha de cobrança da patota se ainda não existir. Nulo se a patota não existe.</summary>
    Task<GroupBillingEntity?> EnsureGroupAsync(Guid groupId, CancellationToken ct);

    Task<GroupBillingSnapshot?> GetSnapshotAsync(Guid groupId, CancellationToken ct);

    Task<GroupBillingStatusDto?> GetStatusAsync(Guid groupId, Guid? userId, string? platform, CancellationToken ct);

    Task<Result<GroupBillingStatusDto>> SubmitPurchaseAsync(Guid groupId, Guid userId, SubmitPurchaseDto dto, string? platform, CancellationToken ct);

    Task<RestoreResultDto> RestoreAsync(Guid userId, RestorePurchasesDto dto, CancellationToken ct);

    Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(Guid userId, CancellationToken ct);

    /// <summary>Aplica uma transação já conferida com a loja (compra, restaurar, aviso da loja, reprocesso).</summary>
    Task<BillingApplyResult> ApplyVerifiedAsync(VerifiedStoreTransaction tx, Guid? requestedGroupId, Guid? userId, CancellationToken ct);

    /// <summary>Marca como reembolsada a cobrança com este ID de transação (aviso de reembolso da loja).</summary>
    Task<BillingApplyResult> RefundTransactionAsync(BillingStore store, string transactionId, string? reason, DateTime when, CancellationToken ct);

    Task RecomputePaidUntilAsync(Guid groupId, CancellationToken ct);

    IStoreVerifier? GetVerifier(BillingStore store);
}

public sealed record BillingApplyResult(bool Success, Guid? GroupId, string? Error, string? Code, bool Transient = false)
{
    public static BillingApplyResult Ok(Guid groupId) => new(true, groupId, null, null);
    public static BillingApplyResult Fail(string error, string code, Guid? groupId = null, bool transient = false) =>
        new(false, groupId, error, code, transient);
}

public static class BillingCodes
{
    public const string PatotaVencida = "PATOTA_VENCIDA";
    public const string StoreUnavailable = "STORE_UNAVAILABLE";
    public const string InvalidPurchase = "INVALID_PURCHASE";
    public const string UnknownProduct = "UNKNOWN_PRODUCT";
    public const string OtherGroup = "SUBSCRIPTION_OTHER_GROUP";
    public const string GroupMismatch = "PURCHASE_GROUP_MISMATCH";
    public const string NoGroup = "PURCHASE_WITHOUT_GROUP";
    public const string GroupNotFound = "GROUP_NOT_FOUND";
}

public sealed class BillingService : IBillingService
{
    /// <summary>Bratnava FC: plano vitalício (isenção permanente), decidido pelo Luis.</summary>
    public static readonly Guid BratnavaGroupId = Guid.Parse("3f401edf-d309-4bae-97d8-28eae0da7a8a");

    private const string SettingsCacheKey = "billing:gate-settings";

    private readonly AppDbContext _db;
    private readonly IEnumerable<IStoreVerifier> _verifiers;
    private readonly IMemoryCache _cache;
    private readonly BillingOptions _options;
    private readonly ILogger<BillingService> _logger;
    private readonly Func<DateTime> _clock;

    public BillingService(AppDbContext db, IEnumerable<IStoreVerifier> verifiers, IMemoryCache cache,
        IOptions<BillingOptions> options, ILogger<BillingService> logger)
        : this(db, verifiers, cache, options, logger, () => DateTime.UtcNow) { }

    internal BillingService(AppDbContext db, IEnumerable<IStoreVerifier> verifiers, IMemoryCache cache,
        IOptions<BillingOptions> options, ILogger<BillingService> logger, Func<DateTime> clock)
    {
        _db = db;
        _verifiers = verifiers;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    public IStoreVerifier? GetVerifier(BillingStore store) => _verifiers.FirstOrDefault(v => v.Store == store);

    private bool SimulatedEnabled => GetVerifier(BillingStore.Simulated)?.IsAvailable == true;

    // ── Configuração ────────────────────────────────────────────────────────────

    public async Task<BillingSettingsEntity> GetSettingsAsync(CancellationToken ct)
    {
        var settings = await _db.BillingSettings.FirstOrDefaultAsync(s => s.Id == BillingSettingsEntity.SingletonId, ct);
        if (settings != null) return settings;

        // A migration já cria a linha; isto cobre o banco de teste em memória.
        settings = BillingSettingsEntity.CreateDefault(_clock());
        _db.BillingSettings.Add(settings);
        await _db.SaveChangesAsync(ct);
        return settings;
    }

    public async Task<(bool EnforcementEnabled, int GraceDays)> GetGateSettingsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(SettingsCacheKey, out (bool, int) cached))
            return cached;

        var s = await GetSettingsAsync(ct);
        var value = (s.EnforcementEnabled, s.GraceDays);
        _cache.Set(SettingsCacheKey, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30),
            Size = 1,
        });
        return value;
    }

    public void InvalidateSettingsCache() => _cache.Remove(SettingsCacheKey);

    // ── Situação da patota ──────────────────────────────────────────────────────

    public async Task<GroupBillingEntity?> EnsureGroupAsync(Guid groupId, CancellationToken ct)
    {
        var billing = await _db.GroupBillings.FirstOrDefaultAsync(b => b.GroupId == groupId, ct);
        if (billing != null) return billing;

        var group = await _db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.CreateDate, g.CreatedByUserId })
            .FirstOrDefaultAsync(ct);
        if (group == null) return null;

        var settings = await GetSettingsAsync(ct);
        var now = _clock();

        // Patota anterior ao lançamento ganha o teste a partir de agora; a nova, da criação.
        var start = group.CreateDate >= settings.LaunchedAt ? group.CreateDate : now;
        if (start > now) start = now;

        // Um teste grátis por usuário criador. Patota antiga (anterior ao lançamento) não
        // perde o teste por isso, mas também conta como o teste do criador.
        var trialGranted = true;
        if (group.CreatedByUserId is { } creatorId)
        {
            var previous = await _db.BillingTrialGrants.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == creatorId, ct);
            if (previous != null && previous.GroupId != groupId && group.CreateDate >= settings.LaunchedAt)
                trialGranted = false;
            if (previous == null)
                _db.BillingTrialGrants.Add(new BillingTrialGrantEntity(creatorId, groupId, now));
        }

        billing = new GroupBillingEntity(groupId, start, settings.TrialDays, trialGranted);
        if (groupId == BratnavaGroupId)
            billing.SetLifetime(true, "Bratnava FC: plano vitalício");

        _db.GroupBillings.Add(billing);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Duas requisições criaram ao mesmo tempo: fica a que chegou primeiro.
            _db.ChangeTracker.Clear();
            return await _db.GroupBillings.FirstOrDefaultAsync(b => b.GroupId == groupId, ct);
        }

        return billing;
    }

    public async Task<GroupBillingSnapshot?> GetSnapshotAsync(Guid groupId, CancellationToken ct)
    {
        var billing = await EnsureGroupAsync(groupId, ct);
        if (billing == null) return null;
        var (enforcement, grace) = await GetGateSettingsAsync(ct);
        return BillingStatusCalculator.Calculate(billing, grace, enforcement, _clock());
    }

    public async Task<GroupBillingStatusDto?> GetStatusAsync(Guid groupId, Guid? userId, string? platform, CancellationToken ct)
    {
        var billing = await EnsureGroupAsync(groupId, ct);
        if (billing == null) return null;

        var settings = await GetSettingsAsync(ct);
        var now = _clock();
        var snap = BillingStatusCalculator.Calculate(billing, settings.GraceDays, settings.EnforcementEnabled, now);

        var subs = await _db.StoreSubscriptions.AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .OrderByDescending(s => s.ExpiresAt)
            .ToListAsync(ct);

        var current = subs.FirstOrDefault(s => IsLive(s, now)) ?? subs.FirstOrDefault();

        var products = await _db.BillingProducts.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var allProducts = await _db.BillingProducts.AsNoTracking().ToListAsync(ct);
        int MonthsOf(BillingStore store, string productId) =>
            allProducts.FirstOrDefault(p => p.Store == store && p.ProductId == productId)?.Months ?? 1;

        var storeForPlatform = PlatformStore(platform);
        var visibleProducts = products
            .Where(p => storeForPlatform == null ? p.Store != BillingStore.Simulated : p.Store == storeForPlatform)
            .Concat(SimulatedEnabled ? products.Where(p => p.Store == BillingStore.Simulated) : [])
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Store).ThenBy(p => p.Months)
            .Select(p => new BillingProductDto(StoreName(p.Store), p.ProductId, p.Months, p.ReferencePriceCents / 100m))
            .ToList();

        var purchases = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.PurchasedAt)
            .Take(50)
            .ToListAsync(ct);

        var userIds = purchases.Select(p => p.UserId).Concat(subs.Select(s => s.PayerUserId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var names = await UserNamesAsync(userIds, ct);

        BillingSubscriptionDto? subDto = current == null ? null : new BillingSubscriptionDto(
            current.Id, StoreName(current.Store), current.ProductId, MonthsOf(current.Store, current.ProductId),
            current.AutoRenewEnabled, current.ExpiresAt, current.State.ToString(),
            current.PayerUserId is { } pid && names.TryGetValue(pid, out var pn) ? pn : null,
            userId != null && current.PayerUserId == userId);

        // Pode comprar? Não, se a patota já tem uma assinatura que vai renovar sozinha
        // (seria cobrar duas vezes) ou se a conta deste usuário já paga outra patota na
        // mesma loja (a loja só deixa uma assinatura deste app por conta).
        string? blocked = null;
        if (snap.Status == GroupBillingStatus.Lifetime)
            blocked = "Esta patota tem plano vitalício.";
        else if (subs.FirstOrDefault(s => IsLive(s, now) && s.AutoRenewEnabled) is { } renewing)
            blocked = renewing.PayerUserId == userId
                ? "Você já assina o plano desta patota. A renovação é automática."
                : $"Esta patota já tem assinatura ativa{(renewing.PayerUserId is { } rp && names.TryGetValue(rp, out var rn) ? $", paga por {rn}" : "")}. A renovação é automática.";
        else if (userId != null && storeForPlatform is { } store)
        {
            var other = await _db.StoreSubscriptions.AsNoTracking()
                .Where(s => s.PayerUserId == userId && s.Store == store && s.GroupId != groupId)
                .ToListAsync(ct);
            var live = other.FirstOrDefault(s => IsLive(s, now) && s.AutoRenewEnabled);
            if (live != null)
            {
                var groupName = await _db.Groups.AsNoTracking().Where(g => g.Id == live.GroupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
                blocked = $"Sua conta da {StoreLabel(store)} já paga o plano da patota {groupName ?? "outra patota"}. "
                          + "A loja só permite uma assinatura por conta: outro admin ou financeiro pode pagar esta.";
            }
        }

        var history = purchases.Select(p => new BillingHistoryItemDto(
            p.Id, p.PurchasedAt, p.ExpiresAt, p.Months, StoreName(p.Store), p.State.ToString().ToLowerInvariant(),
            p.UserId is { } u && names.TryGetValue(u, out var n) ? n : null,
            p.PriceMicros is { } micros ? micros / 1_000_000m : null, p.Currency)).ToList();

        var renewsAlone = current != null && IsLive(current, now) && current.AutoRenewEnabled;

        return new GroupBillingStatusDto(
            groupId,
            StatusName(snap.Status),
            billing.IsLifetime,
            snap.AccessEndsAt,
            billing.IsLifetime ? null : snap.AccessEndsOn.ToString("yyyy-MM-dd"),
            snap.GraceEndsAt,
            billing.IsLifetime ? null : snap.DaysRemaining,
            billing.TrialEndsAt,
            billing.TrialGranted,
            billing.PaidUntil,
            snap.IsReadOnly,
            settings.EnforcementEnabled,
            settings.GraceDays,
            ShowBanner: !billing.IsLifetime && !renewsAlone && snap.DaysRemaining <= 7,
            subDto,
            CanPurchase: blocked == null && visibleProducts.Count > 0,
            blocked,
            visibleProducts,
            SimulatedEnabled,
            history,
            _options.TermsUrl,
            _options.PrivacyUrl);
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(Guid userId, CancellationToken ct)
    {
        var now = _clock();
        var subs = await _db.StoreSubscriptions.AsNoTracking().Where(s => s.PayerUserId == userId).ToListAsync(ct);
        var live = subs.Where(s => IsLive(s, now)).ToList();
        var groupIds = live.Select(s => s.GroupId).Distinct().ToList();
        var groups = await _db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        return live.Select(s => new MySubscriptionDto(
            s.GroupId, groups.GetValueOrDefault(s.GroupId, "(patota apagada)"), StoreName(s.Store), s.ProductId, s.ExpiresAt, s.AutoRenewEnabled)).ToList();
    }

    // ── Compra ──────────────────────────────────────────────────────────────────

    public async Task<Result<GroupBillingStatusDto>> SubmitPurchaseAsync(Guid groupId, Guid userId, SubmitPurchaseDto dto, string? platform, CancellationToken ct)
    {
        if (!TryParseStore(dto.Store, out var store))
            return Result<GroupBillingStatusDto>.Fail("Loja inválida.");
        if (string.IsNullOrWhiteSpace(dto.ProductId))
            return Result<GroupBillingStatusDto>.Fail("Informe o produto.");

        var verifier = GetVerifier(store);
        if (verifier == null || !verifier.IsAvailable)
            return Result<GroupBillingStatusDto>.Fail(
                store == BillingStore.Simulated ? "Compra simulada não está disponível neste ambiente." : $"Pagamento pela {StoreLabel(store)} ainda não está disponível.",
                ResultStatus.BadRequest, [BillingCodes.StoreUnavailable]);

        if (await EnsureGroupAsync(groupId, ct) == null)
            return Result<GroupBillingStatusDto>.Fail("Patota não encontrada.", ResultStatus.NotFound, [BillingCodes.GroupNotFound]);

        VerifiedStoreTransaction tx;
        try
        {
            tx = await verifier.VerifyAsync(new StorePurchaseProof(dto.ProductId.Trim(), dto.TransactionId, dto.PurchaseToken, groupId), ct);
        }
        catch (StoreVerificationException ex)
        {
            _logger.LogWarning("[Billing] Compra recusada na conferência. Group={GroupId} Store={Store} Erro={Error}", groupId, store, ex.Message);
            return Result<GroupBillingStatusDto>.Fail(
                ex.IsTransient ? "Não consegui confirmar a compra com a loja agora. Tente \"Restaurar compras\" daqui a pouco." : ex.Message,
                ResultStatus.BadRequest, [BillingCodes.InvalidPurchase]);
        }

        var applied = await ApplyVerifiedAsync(tx, groupId, userId, ct);
        if (!applied.Success)
            return Result<GroupBillingStatusDto>.Fail(applied.Error!,
                applied.Code == BillingCodes.OtherGroup ? ResultStatus.Conflict : ResultStatus.BadRequest,
                [applied.Code!]);

        var status = await GetStatusAsync(groupId, userId, platform, ct);
        return Result<GroupBillingStatusDto>.Ok(status!, "Pagamento confirmado.");
    }

    public async Task<RestoreResultDto> RestoreAsync(Guid userId, RestorePurchasesDto dto, CancellationToken ct)
    {
        var errors = new List<string>();
        var groups = new HashSet<Guid>();

        if (!TryParseStore(dto.Store, out var store) || GetVerifier(store) is not { IsAvailable: true } verifier)
            return new RestoreResultDto(0, [], ["Esta loja ainda não está disponível."]);

        foreach (var item in (dto.Items ?? []).Take(50))
        {
            try
            {
                var tx = await verifier.VerifyAsync(new StorePurchaseProof(item.ProductId, item.TransactionId, item.PurchaseToken, null), ct);
                var applied = await ApplyVerifiedAsync(tx, null, userId, ct);
                if (applied.Success && applied.GroupId is { } g) groups.Add(g);
                else if (applied.Error != null) errors.Add(applied.Error);
            }
            catch (StoreVerificationException ex)
            {
                errors.Add(ex.Message);
            }
        }

        var names = await _db.Groups.AsNoTracking().Where(g => groups.Contains(g.Id)).Select(g => g.Name).ToListAsync(ct);
        return new RestoreResultDto(groups.Count, names, errors.Distinct().ToList());
    }

    public async Task<BillingApplyResult> ApplyVerifiedAsync(VerifiedStoreTransaction tx, Guid? requestedGroupId, Guid? userId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(tx, requestedGroupId, userId, ct);
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsUniqueViolation(ex))
            {
                // O app e o aviso da loja chegaram juntos com a mesma transação: a segunda
                // tentativa já encontra o registro e só atualiza.
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<BillingApplyResult> ApplyOnceAsync(VerifiedStoreTransaction tx, Guid? requestedGroupId, Guid? userId, CancellationToken ct)
    {
        var product = await _db.BillingProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Store == tx.Store && p.ProductId == tx.ProductId, ct);
        if (product == null)
            return BillingApplyResult.Fail($"Produto {tx.ProductId} não está cadastrado para a {StoreLabel(tx.Store)}.", BillingCodes.UnknownProduct);

        var sub = await _db.StoreSubscriptions
            .FirstOrDefaultAsync(s => s.Store == tx.Store && s.StoreSubscriptionId == tx.StoreSubscriptionId, ct);

        // De qual patota é esta assinatura? Já ligada: a mesma de sempre. Nova: a que o app
        // pediu, conferida com a que a loja gravou na compra.
        Guid groupId;
        if (sub != null)
        {
            if (requestedGroupId != null && requestedGroupId != sub.GroupId)
            {
                var other = await _db.Groups.AsNoTracking().Where(g => g.Id == sub.GroupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
                return BillingApplyResult.Fail(
                    $"Esta assinatura da {StoreLabel(tx.Store)} já paga a patota {other ?? "outra patota"}. Cada conta da loja paga uma patota por vez.",
                    BillingCodes.OtherGroup, sub.GroupId);
            }
            groupId = sub.GroupId;
        }
        else
        {
            if (requestedGroupId != null && tx.GroupHint != null && tx.GroupHint != requestedGroupId)
                return BillingApplyResult.Fail("Esta compra foi feita para outra patota.", BillingCodes.GroupMismatch, tx.GroupHint);

            Guid? linkedGroup = null;
            if (tx.LinkedStoreSubscriptionId is { } linked)
                linkedGroup = await _db.StoreSubscriptions.AsNoTracking()
                    .Where(s => s.Store == tx.Store && s.StoreSubscriptionId == linked)
                    .Select(s => (Guid?)s.GroupId).FirstOrDefaultAsync(ct);

            var resolved = requestedGroupId ?? tx.GroupHint ?? linkedGroup;
            if (resolved == null)
                return BillingApplyResult.Fail("Não sei de qual patota é esta compra.", BillingCodes.NoGroup);
            groupId = resolved.Value;

            if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
                return BillingApplyResult.Fail("Patota da compra não existe mais.", BillingCodes.GroupNotFound, groupId);

            sub = new StoreSubscriptionEntity(groupId, userId, tx.Store, tx.StoreSubscriptionId, tx.ProductId, tx.Environment);
            _db.StoreSubscriptions.Add(sub);
        }

        if (await EnsureGroupAsync(groupId, ct) == null)
            return BillingApplyResult.Fail("Patota da compra não existe mais.", BillingCodes.GroupNotFound, groupId);

        sub.SetPayerIfMissing(userId);
        sub.Sync(tx.ProductId, tx.SubscriptionState, tx.AutoRenewEnabled, tx.ExpiresAt);

        // Cada cobrança uma vez só (índice único Store + TransactionId).
        var purchase = await _db.BillingPurchases
            .FirstOrDefaultAsync(p => p.Store == tx.Store && p.TransactionId == tx.TransactionId, ct);

        var purchasedAt = tx.PurchasedAt;
        if (tx.Store == BillingStore.Google)
        {
            // O Google não diz quando cada renovação foi cobrada; o período é o mês que
            // termina no vencimento.
            var periodStart = tx.ExpiresAt.AddMonths(-product.Months);
            if (periodStart > purchasedAt) purchasedAt = periodStart;
        }

        if (purchase == null)
        {
            purchase = new BillingPurchaseEntity(
                groupId, sub.Id, userId ?? sub.PayerUserId, tx.Store, tx.ProductId, tx.TransactionId, product.Months,
                purchasedAt, tx.ExpiresAt,
                tx.IsPending ? BillingPurchaseState.Pending : BillingPurchaseState.Confirmed,
                tx.PriceMicros, tx.Currency, tx.Environment);
            _db.BillingPurchases.Add(purchase);
        }
        else if (purchase.GroupId != groupId)
        {
            return BillingApplyResult.Fail("Esta cobrança já foi usada em outra patota.", BillingCodes.OtherGroup, purchase.GroupId);
        }
        else if (!tx.IsPending)
        {
            purchase.Confirm(tx.ExpiresAt);
        }

        if (tx.RevokedAt is { } revokedAt && purchase.State != BillingPurchaseState.Refunded)
            purchase.Refund(revokedAt, tx.RevocationReason);

        await _db.SaveChangesAsync(ct);
        await RecomputePaidUntilAsync(groupId, ct);

        if (tx.NeedsAcknowledge && sub.AcknowledgedAt == null && GetVerifier(tx.Store) is { } verifier)
        {
            try
            {
                await verifier.AcknowledgeAsync(tx, ct);
                sub.MarkAcknowledged();
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Fica no painel de problemas; o job diário tenta de novo (o Google dá 3 dias).
                _logger.LogWarning(ex, "[Billing] Falha ao confirmar (acknowledge) a compra {Token} no Google.", tx.StoreSubscriptionId);
            }
        }
        else if (!tx.NeedsAcknowledge && sub.AcknowledgedAt == null && tx.Store == BillingStore.Google && !tx.IsPending)
        {
            sub.MarkAcknowledged();
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "[Billing] Transação aplicada. Group={GroupId} Store={Store} Product={Product} Tx={Tx} Expira={Expires} Estado={State}",
            groupId, tx.Store, tx.ProductId, tx.TransactionId, tx.ExpiresAt, purchase.State);

        return BillingApplyResult.Ok(groupId);
    }

    public async Task<BillingApplyResult> RefundTransactionAsync(BillingStore store, string transactionId, string? reason, DateTime when, CancellationToken ct)
    {
        var purchase = await _db.BillingPurchases.FirstOrDefaultAsync(p => p.Store == store && p.TransactionId == transactionId, ct);
        if (purchase == null)
            return BillingApplyResult.Fail("Cobrança reembolsada não encontrada.", BillingCodes.InvalidPurchase);

        if (purchase.State != BillingPurchaseState.Refunded)
        {
            purchase.Refund(when, reason ?? "Reembolso");
            await _db.SaveChangesAsync(ct);
            await RecomputePaidUntilAsync(purchase.GroupId, ct);
        }
        return BillingApplyResult.Ok(purchase.GroupId);
    }

    /// <summary>
    /// Período pago = maior vencimento entre as cobranças confirmadas. Reembolso tira a
    /// cobrança da conta, e o período volta para o vencimento da anterior.
    /// </summary>
    public async Task RecomputePaidUntilAsync(Guid groupId, CancellationToken ct)
    {
        var billing = await _db.GroupBillings.FirstOrDefaultAsync(b => b.GroupId == groupId, ct);
        if (billing == null) return;

        var paidUntil = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.State == BillingPurchaseState.Confirmed)
            .Select(p => (DateTime?)p.ExpiresAt)
            .MaxAsync(ct);

        if (billing.PaidUntil != paidUntil)
        {
            billing.SetPaidUntil(paidUntil);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────

    /// <summary>A assinatura ainda dá acesso ou vai voltar a dar (a loja está tentando cobrar).</summary>
    public static bool IsLive(StoreSubscriptionEntity s, DateTime now) => s.State switch
    {
        StoreSubscriptionState.Active => s.ExpiresAt == null || s.ExpiresAt > now,
        StoreSubscriptionState.GracePeriod or StoreSubscriptionState.BillingRetry => true,
        _ => false,
    };

    private async Task<Dictionary<Guid, string>> UserNamesAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);

    public static bool TryParseStore(string? raw, out BillingStore store)
    {
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "apple": case "ios": case "appstore": store = BillingStore.Apple; return true;
            case "google": case "android": case "googleplay": case "play": store = BillingStore.Google; return true;
            case "simulated": case "simulada": store = BillingStore.Simulated; return true;
            default: store = default; return false;
        }
    }

    private static BillingStore? PlatformStore(string? platform) => (platform ?? "").Trim().ToLowerInvariant() switch
    {
        "ios" or "apple" => BillingStore.Apple,
        "android" or "google" => BillingStore.Google,
        _ => null,
    };

    public static string StoreName(BillingStore s) => s switch
    {
        BillingStore.Apple => "apple",
        BillingStore.Google => "google",
        _ => "simulated",
    };

    public static string StoreLabel(BillingStore s) => s switch
    {
        BillingStore.Apple => "App Store",
        BillingStore.Google => "Google Play",
        _ => "loja simulada",
    };

    public static string StatusName(GroupBillingStatus s) => s switch
    {
        GroupBillingStatus.Lifetime => "lifetime",
        GroupBillingStatus.Trial => "trial",
        GroupBillingStatus.Active => "active",
        GroupBillingStatus.Grace => "grace",
        _ => "expired",
    };

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || ex.InnerException?.Message.Contains("same key", StringComparison.OrdinalIgnoreCase) == true;
}
