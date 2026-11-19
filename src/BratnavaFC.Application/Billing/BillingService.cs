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

    /// <summary>Aplica uma compra já conferida com a loja (compra, restaurar, aviso da loja, reprocesso).</summary>
    Task<BillingApplyResult> ApplyVerifiedAsync(VerifiedStorePurchase purchase, Guid? requestedGroupId, Guid? userId, CancellationToken ct);

    /// <summary>Marca como reembolsada a compra com este ID de transação (aviso de reembolso da loja).</summary>
    Task<BillingApplyResult> RefundTransactionAsync(BillingStore store, string transactionId, string? reason, DateTime when, CancellationToken ct);

    /// <summary>Refaz a fila de períodos da patota (pacotes confirmados + dias dados pelo GodMode).</summary>
    Task RecomputeAsync(Guid groupId, CancellationToken ct);

    /// <summary>Google: consome as compras confirmadas que ainda não foram consumidas.</summary>
    Task<int> ConsumePendingAsync(CancellationToken ct);

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
    public const string OtherGroup = "PURCHASE_OTHER_GROUP";
    public const string GroupMismatch = "PURCHASE_GROUP_MISMATCH";
    public const string NoGroup = "PURCHASE_WITHOUT_GROUP";
    public const string GroupNotFound = "GROUP_NOT_FOUND";
}

/// <summary>
/// Cobrança por pacote avulso (1, 3, 6 ou 12 meses) que soma ao vencimento: Apple
/// non-renewing subscription e Google produto consumível. Qualquer admin ou financeiro da
/// patota paga, inclusive antes de vencer; cada compra conta uma vez só (ID de transação
/// único) e o reembolso tira exatamente aqueles meses.
/// </summary>
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

        // Comprar agora soma a partir do vencimento atual (ou de agora, se já venceu).
        var from = billing.AccessEndsAt > now ? billing.AccessEndsAt : now;

        var storeForPlatform = PlatformStore(platform);
        var products = await _db.BillingProducts.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var visibleProducts = products
            .Where(p => storeForPlatform == null ? p.Store != BillingStore.Simulated : p.Store == storeForPlatform)
            .Concat(SimulatedEnabled ? products.Where(p => p.Store == BillingStore.Simulated) : [])
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Store).ThenBy(p => p.Months)
            .Select(p => new BillingProductDto(StoreName(p.Store), p.ProductId, p.Months, p.ReferencePriceCents / 100m,
                BillingStatusCalculator.BrasiliaDate(from.AddMonths(p.Months).AddTicks(-1)).ToString("yyyy-MM-dd")))
            .ToList();

        var purchases = await _db.BillingPurchases.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.PurchasedAt)
            .Take(50)
            .ToListAsync(ct);
        var names = await UserNamesAsync(purchases.Select(p => p.UserId), ct);

        var history = purchases.Select(p => new BillingHistoryItemDto(
            p.Id, p.PurchasedAt, p.PeriodStart, p.PeriodEnd, p.Months, StoreName(p.Store), p.State.ToString().ToLowerInvariant(),
            p.UserId is { } u && names.TryGetValue(u, out var n) ? n : null,
            p.PriceMicros is { } micros ? micros / 1_000_000m : null, p.Currency)).ToList();

        var blocked = billing.IsLifetime ? "Esta patota tem plano vitalício." : null;

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
            ShowBanner: !billing.IsLifetime && snap.DaysRemaining <= 7,
            CanPurchase: blocked == null && visibleProducts.Count > 0,
            blocked,
            visibleProducts,
            SimulatedEnabled,
            history,
            _options.TermsUrl,
            _options.PrivacyUrl);
    }

    // ── Compra ──────────────────────────────────────────────────────────────────

    public async Task<Result<GroupBillingStatusDto>> SubmitPurchaseAsync(Guid groupId, Guid userId, SubmitPurchaseDto dto, string? platform, CancellationToken ct)
    {
        if (!TryParseStore(dto.Store, out var store))
            return Result<GroupBillingStatusDto>.Fail("Loja inválida.");
        if (string.IsNullOrWhiteSpace(dto.ProductId))
            return Result<GroupBillingStatusDto>.Fail("Informe o pacote.");

        var verifier = GetVerifier(store);
        if (verifier == null || !verifier.IsAvailable)
            return Result<GroupBillingStatusDto>.Fail(
                store == BillingStore.Simulated ? "Compra simulada não está disponível neste ambiente." : $"Pagamento pela {StoreLabel(store)} ainda não está disponível.",
                ResultStatus.BadRequest, [BillingCodes.StoreUnavailable]);

        if (await EnsureGroupAsync(groupId, ct) == null)
            return Result<GroupBillingStatusDto>.Fail("Patota não encontrada.", ResultStatus.NotFound, [BillingCodes.GroupNotFound]);

        VerifiedStorePurchase purchase;
        try
        {
            purchase = await verifier.VerifyAsync(new StorePurchaseProof(dto.ProductId.Trim(), dto.TransactionId, dto.PurchaseToken, groupId), ct);
        }
        catch (StoreVerificationException ex)
        {
            _logger.LogWarning("[Billing] Compra recusada na conferência. Group={GroupId} Store={Store} Erro={Error}", groupId, store, ex.Message);
            return Result<GroupBillingStatusDto>.Fail(
                ex.IsTransient ? "Não consegui confirmar a compra com a loja agora. Tente \"Restaurar compras\" daqui a pouco." : ex.Message,
                ResultStatus.BadRequest, [BillingCodes.InvalidPurchase]);
        }

        var applied = await ApplyVerifiedAsync(purchase, groupId, userId, ct);
        if (!applied.Success)
            return Result<GroupBillingStatusDto>.Fail(applied.Error!,
                applied.Code == BillingCodes.OtherGroup ? ResultStatus.Conflict : ResultStatus.BadRequest,
                [applied.Code!]);

        var status = await GetStatusAsync(groupId, userId, platform, ct);
        return Result<GroupBillingStatusDto>.Ok(status!, purchase.IsPending ? "Pagamento pendente na loja." : "Pagamento confirmado.");
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
                var purchase = await verifier.VerifyAsync(new StorePurchaseProof(item.ProductId, item.TransactionId, item.PurchaseToken, null), ct);
                var applied = await ApplyVerifiedAsync(purchase, null, userId, ct);
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

    public async Task<BillingApplyResult> ApplyVerifiedAsync(VerifiedStorePurchase purchase, Guid? requestedGroupId, Guid? userId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(purchase, requestedGroupId, userId, ct);
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsUniqueViolation(ex))
            {
                // O app e o aviso da loja chegaram juntos com a mesma compra: a segunda
                // tentativa já encontra o registro e só atualiza.
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<BillingApplyResult> ApplyOnceAsync(VerifiedStorePurchase tx, Guid? requestedGroupId, Guid? userId, CancellationToken ct)
    {
        var product = await _db.BillingProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Store == tx.Store && p.ProductId == tx.ProductId, ct);
        if (product == null)
            return BillingApplyResult.Fail($"Pacote {tx.ProductId} não está cadastrado para a {StoreLabel(tx.Store)}.", BillingCodes.UnknownProduct);

        var purchase = await _db.BillingPurchases
            .FirstOrDefaultAsync(p => p.Store == tx.Store && p.TransactionId == tx.TransactionId, ct);

        Guid groupId;
        if (purchase != null)
        {
            // Já registrada (reenvio do app, restaurar ou aviso da loja): só atualiza o estado.
            if (requestedGroupId != null && requestedGroupId != purchase.GroupId)
            {
                var other = await _db.Groups.AsNoTracking().Where(g => g.Id == purchase.GroupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
                return BillingApplyResult.Fail($"Esta compra já foi usada na patota {other ?? "(apagada)"}.", BillingCodes.OtherGroup, purchase.GroupId);
            }
            groupId = purchase.GroupId;
            if (!tx.IsPending) purchase.Confirm();
        }
        else
        {
            // A patota gravada na loja na hora da compra manda: protege contra usar o
            // comprovante de uma patota em outra.
            if (requestedGroupId != null && tx.GroupHint != null && tx.GroupHint != requestedGroupId)
                return BillingApplyResult.Fail("Esta compra foi feita para outra patota.", BillingCodes.GroupMismatch, tx.GroupHint);

            var resolved = tx.GroupHint ?? requestedGroupId;
            if (resolved == null)
                return BillingApplyResult.Fail("Não sei de qual patota é esta compra.", BillingCodes.NoGroup);
            groupId = resolved.Value;

            if (await EnsureGroupAsync(groupId, ct) == null)
                return BillingApplyResult.Fail("Patota da compra não existe mais.", BillingCodes.GroupNotFound, groupId);

            purchase = new BillingPurchaseEntity(
                groupId, userId, tx.Store, tx.ProductId, tx.TransactionId, tx.PurchaseToken, product.Months, tx.PurchasedAt,
                tx.IsPending ? BillingPurchaseState.Pending : BillingPurchaseState.Confirmed,
                tx.PriceMicros, tx.Currency, tx.Environment, needsConsume: tx.NeedsConsume);
            _db.BillingPurchases.Add(purchase);
        }

        if (tx.RevokedAt is { } revokedAt && purchase.State != BillingPurchaseState.Refunded)
            purchase.Refund(revokedAt, tx.RevocationReason);
        else if (tx.Store == BillingStore.Google && !tx.NeedsConsume && purchase.ConsumedAt == null && !tx.IsPending)
            purchase.MarkConsumed(); // já consumida na loja (ex.: por outra tentativa)

        await _db.SaveChangesAsync(ct);
        await RecomputeAsync(groupId, ct);

        if (purchase.NeedsConsume && tx.PurchaseToken != null)
            await TryConsumeAsync(purchase, ct);

        _logger.LogInformation(
            "[Billing] Pacote aplicado. Group={GroupId} Store={Store} Product={Product} Tx={Tx} Estado={State}",
            groupId, tx.Store, tx.ProductId, tx.TransactionId, purchase.State);

        return BillingApplyResult.Ok(groupId);
    }

    private async Task<bool> TryConsumeAsync(BillingPurchaseEntity purchase, CancellationToken ct)
    {
        if (GetVerifier(purchase.Store) is not { IsAvailable: true } verifier || purchase.PurchaseToken == null) return false;
        try
        {
            await verifier.ConsumeAsync(purchase.ProductId, purchase.PurchaseToken, ct);
            purchase.MarkConsumed();
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            // Fica no painel de problemas; o job diário tenta de novo (o Google dá 3 dias).
            _logger.LogWarning(ex, "[Billing] Falha ao consumir a compra {Tx} no Google.", purchase.TransactionId);
            return false;
        }
    }

    public async Task<int> ConsumePendingAsync(CancellationToken ct)
    {
        var pending = await _db.BillingPurchases
            .Where(p => p.Store == BillingStore.Google && p.ConsumedAt == null && p.State == BillingPurchaseState.Confirmed)
            .ToListAsync(ct);
        var done = 0;
        foreach (var p in pending)
            if (await TryConsumeAsync(p, ct)) done++;
        return done;
    }

    public async Task<BillingApplyResult> RefundTransactionAsync(BillingStore store, string transactionId, string? reason, DateTime when, CancellationToken ct)
    {
        var purchase = await _db.BillingPurchases.FirstOrDefaultAsync(p => p.Store == store && p.TransactionId == transactionId, ct);
        if (purchase == null)
            return BillingApplyResult.Fail("Compra reembolsada não encontrada.", BillingCodes.InvalidPurchase);

        if (purchase.State != BillingPurchaseState.Refunded)
        {
            purchase.Refund(when, reason ?? "Reembolso");
            await _db.SaveChangesAsync(ct);
            await RecomputeAsync(purchase.GroupId, ct);
        }
        return BillingApplyResult.Ok(purchase.GroupId);
    }

    /// <summary>
    /// Fila de períodos: pacotes confirmados e dias dados pelo GodMode, na ordem em que
    /// aconteceram, cada um somando a partir do fim do anterior (ou do momento, se a patota
    /// estava vencida). Refeita do zero, então reembolso tira exatamente aquele pacote.
    /// </summary>
    public async Task RecomputeAsync(Guid groupId, CancellationToken ct)
    {
        var billing = await _db.GroupBillings.FirstOrDefaultAsync(b => b.GroupId == groupId, ct);
        if (billing == null) return;

        var purchases = await _db.BillingPurchases.Where(p => p.GroupId == groupId).ToListAsync(ct);
        var extensions = await _db.BillingAdjustments.AsNoTracking()
            .Where(a => a.GroupId == groupId && a.Type == BillingAdjustmentType.ExtendDays && a.Days != null)
            .Select(a => new { a.Id, a.CreateDate, a.Days })
            .ToListAsync(ct);

        var confirmed = purchases.Where(p => p.State == BillingPurchaseState.Confirmed).ToList();
        var items = confirmed.Select(p => new BillingTimelineItem(p.Id, p.PurchasedAt, p.Months, 0, true))
            .Concat(extensions.Select(e => new BillingTimelineItem(e.Id, e.CreateDate, 0, e.Days!.Value, false)));

        var (end, periods) = BillingTimeline.Compute(billing.TrialEndsAt, items);
        var byId = periods.ToDictionary(p => p.Id);
        foreach (var p in purchases)
        {
            if (byId.TryGetValue(p.Id, out var period)) p.SetPeriod(period.Start, period.End);
            else p.SetPeriod(null, null);
        }

        billing.SetTimeline(end, confirmed.Count > 0);
        await _db.SaveChangesAsync(ct);
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        return list.Count == 0
            ? []
            : await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);
    }

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
