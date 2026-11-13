using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities.Billing;

/// <summary>
/// Parâmetros da cobrança, numa linha só (Id = 1). Tudo o que o GodMode ajusta pelo site
/// sem deploy: dias de teste, tolerância, lembretes e o interruptor do bloqueio.
/// </summary>
public sealed class BillingSettingsEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;

    /// <summary>Dias de teste grátis de uma patota nova.</summary>
    public int TrialDays { get; private set; } = 30;

    /// <summary>Dias de tolerância depois do vencimento antes de virar somente leitura.</summary>
    public int GraceDays { get; private set; } = 3;

    /// <summary>
    /// Interruptor do bloqueio. Desligado, a API nunca recusa escrita por falta de
    /// pagamento — a cobrança só aparece (status, card, compra), não trava nada.
    /// </summary>
    public bool EnforcementEnabled { get; private set; }

    /// <summary>Lembretes por push para admins e financeiros antes de vencer.</summary>
    public bool RemindersEnabled { get; private set; }

    /// <summary>Dias antes do vencimento em que o lembrete sai, separados por vírgula (0 = no dia).</summary>
    public string ReminderDays { get; private set; } = "7,3,1,0";

    /// <summary>
    /// Momento em que a cobrança entrou no ar. Patota criada antes disso ganha o teste a
    /// partir do primeiro acesso, não da criação (senão já nasceria vencida).
    /// </summary>
    public DateTime LaunchedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    private BillingSettingsEntity() { }

    public static BillingSettingsEntity CreateDefault(DateTime launchedAt) => new() { LaunchedAt = launchedAt };

    public IReadOnlyList<int> GetReminderDays() => ParseReminderDays(ReminderDays);

    public static IReadOnlyList<int> ParseReminderDays(string? raw) =>
        (raw ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var d) ? d : -1)
            .Where(d => d is >= 0 and <= 60)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

    public void Update(int trialDays, int graceDays, bool enforcementEnabled, bool remindersEnabled,
        string reminderDays, Guid? byUserId)
    {
        if (trialDays is < 0 or > 365) throw new ArgumentException("Dias de teste devem ficar entre 0 e 365.");
        if (graceDays is < 0 or > 60) throw new ArgumentException("Tolerância deve ficar entre 0 e 60 dias.");

        TrialDays = trialDays;
        GraceDays = graceDays;
        EnforcementEnabled = enforcementEnabled;
        RemindersEnabled = remindersEnabled;
        ReminderDays = string.Join(",", ParseReminderDays(reminderDays));
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = byUserId;
    }
}

/// <summary>
/// Produto (plano) cadastrado numa loja. O preço real é o da loja; aqui fica só o preço de
/// referência, para o painel. Para trocar o preço cria-se outro produto na loja e troca-se
/// o ativo aqui: só um produto ativo por loja e duração.
/// </summary>
public sealed class BillingProductEntity : BaseEntity
{
    public BillingStore Store { get; private set; }

    /// <summary>ID do produto na loja (ex.: <c>patota_1m</c>).</summary>
    public string ProductId { get; private set; } = null!;

    public int Months { get; private set; }

    /// <summary>Preço de referência em centavos (BRL).</summary>
    public int ReferencePriceCents { get; private set; }

    public bool IsActive { get; private set; }

    private BillingProductEntity() { }

    public BillingProductEntity(BillingStore store, string productId, int months, int referencePriceCents, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("Informe o ID do produto.");
        if (months is not (1 or 3 or 6 or 12)) throw new ArgumentException("Duração deve ser 1, 3, 6 ou 12 meses.");
        if (referencePriceCents < 0) throw new ArgumentException("Preço de referência inválido.");

        Store = store;
        ProductId = productId.Trim();
        Months = months;
        ReferencePriceCents = referencePriceCents;
        IsActive = isActive;
    }

    public void Update(int referencePriceCents, bool isActive)
    {
        if (referencePriceCents < 0) throw new ArgumentException("Preço de referência inválido.");
        ReferencePriceCents = referencePriceCents;
        IsActive = isActive;
        UpdateDate = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdateDate = DateTime.UtcNow;
    }
}

/// <summary>
/// Situação de cobrança de uma patota. Criada no primeiro acesso à cobrança da patota
/// (o teste conta da criação da patota, ou do lançamento para as que já existiam).
///
/// Cobrança por pacote avulso: cada pacote pago soma N meses a partir do vencimento da
/// época (ou de quando foi pago, se já estava vencida). O acesso vale até o fim dessa
/// fila de períodos, calculada por <see cref="BillingTimeline"/>.
/// </summary>
public sealed class GroupBillingEntity
{
    public Guid GroupId { get; private set; }

    public DateTime TrialStartedAt { get; private set; }
    public DateTime TrialEndsAt { get; private set; }

    /// <summary>
    /// Falso quando o criador já tinha usado o teste grátis em outra patota: esta nasce
    /// sem teste (anti-abuso).
    /// </summary>
    public bool TrialGranted { get; private set; }

    /// <summary>
    /// Fim do acesso somando pacotes pagos e dias dados pelo GodMode depois do teste.
    /// Nulo quando não há nenhum dos dois. Recalculado do zero a cada mudança, então um
    /// reembolso tira exatamente os meses daquele pacote.
    /// </summary>
    public DateTime? PaidUntil { get; private set; }

    /// <summary>Verdadeiro se há ao menos um pacote pago confirmado.</summary>
    public bool HasPaidPeriod { get; private set; }

    public bool IsLifetime { get; private set; }
    public string? LifetimeReason { get; private set; }

    /// <summary>Chave do último lembrete enviado (<c>yyyy-MM-dd:dias</c>), para não repetir.</summary>
    public string? LastReminderKey { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private GroupBillingEntity() { }

    public GroupBillingEntity(Guid groupId, DateTime trialStartedAt, int trialDays, bool trialGranted)
    {
        GroupId = groupId;
        TrialStartedAt = trialStartedAt;
        TrialGranted = trialGranted;
        TrialEndsAt = trialGranted ? trialStartedAt.AddDays(Math.Max(0, trialDays)) : trialStartedAt;
        CreatedAt = DateTime.UtcNow;
    }

    public DateTime AccessEndsAt => PaidUntil is { } paid && paid > TrialEndsAt ? paid : TrialEndsAt;

    public void SetTimeline(DateTime? paidUntil, bool hasPaidPeriod)
    {
        PaidUntil = paidUntil;
        HasPaidPeriod = hasPaidPeriod;
        Touch();
    }

    public void SetLifetime(bool enabled, string? reason)
    {
        IsLifetime = enabled;
        LifetimeReason = enabled ? reason?.Trim() : null;
        Touch();
    }

    public void MarkReminded(string key)
    {
        LastReminderKey = key;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

/// <summary>Um item da fila de períodos: pacote pago (meses) ou dias dados pelo GodMode.</summary>
public sealed record BillingTimelineItem(Guid Id, DateTime At, int Months, int Days, bool IsPurchase);

/// <summary>Período calculado de um item da fila.</summary>
public sealed record BillingTimelinePeriod(Guid Id, DateTime Start, DateTime End, bool IsPurchase);

/// <summary>
/// A fila de períodos da patota. Cada item começa no fim do anterior (ou quando foi
/// pago/dado, se a patota já estava vencida) e dura os seus meses ou dias. O primeiro
/// começa depois do teste grátis: pagar durante o teste não desperdiça dia nenhum.
/// </summary>
public static class BillingTimeline
{
    public static (DateTime? End, IReadOnlyList<BillingTimelinePeriod> Periods) Compute(
        DateTime trialEndsAt, IEnumerable<BillingTimelineItem> items)
    {
        var cursor = trialEndsAt;
        var periods = new List<BillingTimelinePeriod>();
        foreach (var item in items.OrderBy(i => i.At).ThenBy(i => i.Id))
        {
            var start = item.At > cursor ? item.At : cursor;
            var end = start.AddMonths(item.Months).AddDays(item.Days);
            periods.Add(new BillingTimelinePeriod(item.Id, start, end, item.IsPurchase));
            cursor = end;
        }
        return (periods.Count == 0 ? null : cursor, periods);
    }
}

/// <summary>
/// Quem já recebeu teste grátis. Separada da patota de propósito: sobrevive à exclusão
/// dela, senão bastaria apagar e recriar para ganhar outro teste.
/// </summary>
public sealed class BillingTrialGrantEntity
{
    public Guid UserId { get; private set; }
    public Guid GroupId { get; private set; }
    public DateTime GrantedAt { get; private set; }

    private BillingTrialGrantEntity() { }

    public BillingTrialGrantEntity(Guid userId, Guid groupId, DateTime grantedAt)
    {
        UserId = userId;
        GroupId = groupId;
        GrantedAt = grantedAt;
    }
}

/// <summary>
/// Um pacote comprado (1, 3, 6 ou 12 meses). O ID da transação é único por loja: a mesma
/// compra reenviada (pelo app, pelo restaurar ou pelo aviso da loja) nunca soma duas vezes.
/// O período (início e fim) é recalculado pela <see cref="BillingTimeline"/>.
/// </summary>
public sealed class BillingPurchaseEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid? UserId { get; private set; }
    public BillingStore Store { get; private set; }
    public string ProductId { get; private set; } = null!;

    /// <summary>Apple: transactionId. Google: orderId. Único por loja.</summary>
    public string TransactionId { get; private set; } = null!;

    /// <summary>Google: purchaseToken (para consumir e reprocessar).</summary>
    public string? PurchaseToken { get; private set; }

    public int Months { get; private set; }
    public DateTime PurchasedAt { get; private set; }
    public DateTime? PeriodStart { get; private set; }
    public DateTime? PeriodEnd { get; private set; }
    public BillingPurchaseState State { get; private set; }

    /// <summary>Preço pago, em micros (1 real = 1.000.000), quando a loja informa.</summary>
    public long? PriceMicros { get; private set; }
    public string? Currency { get; private set; }

    public string Environment { get; private set; } = "Production";

    /// <summary>
    /// Google: consumida na loja (o que também a confirma). Sem isso em 3 dias o Google
    /// reembolsa sozinho. Apple e simulada já nascem com a data preenchida.
    /// </summary>
    public DateTime? ConsumedAt { get; private set; }

    public DateTime? RefundedAt { get; private set; }
    public string? RefundReason { get; private set; }

    private BillingPurchaseEntity() { }

    public BillingPurchaseEntity(Guid groupId, Guid? userId, BillingStore store, string productId, string transactionId,
        string? purchaseToken, int months, DateTime purchasedAt, BillingPurchaseState state, long? priceMicros,
        string? currency, string environment, bool needsConsume)
    {
        GroupId = groupId;
        UserId = userId;
        Store = store;
        ProductId = productId;
        TransactionId = transactionId;
        PurchaseToken = purchaseToken;
        Months = months;
        PurchasedAt = purchasedAt;
        State = state;
        PriceMicros = priceMicros;
        Currency = currency;
        Environment = environment;
        if (!needsConsume) ConsumedAt = purchasedAt;
    }

    public bool NeedsConsume => ConsumedAt == null && State == BillingPurchaseState.Confirmed;

    public void Confirm()
    {
        if (State != BillingPurchaseState.Pending) return;
        State = BillingPurchaseState.Confirmed;
        UpdateDate = DateTime.UtcNow;
    }

    public void SetPeriod(DateTime? start, DateTime? end)
    {
        PeriodStart = start;
        PeriodEnd = end;
    }

    public void MarkConsumed()
    {
        ConsumedAt = DateTime.UtcNow;
        UpdateDate = DateTime.UtcNow;
    }

    public void Refund(DateTime when, string? reason)
    {
        State = BillingPurchaseState.Refunded;
        RefundedAt = when;
        RefundReason = reason;
        UpdateDate = DateTime.UtcNow;
    }

    public void ReverseRefund()
    {
        if (State != BillingPurchaseState.Refunded) return;
        State = BillingPurchaseState.Confirmed;
        RefundedAt = null;
        RefundReason = null;
        UpdateDate = DateTime.UtcNow;
    }
}

/// <summary>Auditoria das ações manuais (GodMode) e de configuração.</summary>
public sealed class BillingAdjustmentEntity : BaseEntity
{
    public const int ReasonMaxLength = 500;

    public Guid? GroupId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public BillingAdjustmentType Type { get; private set; }
    public int? Days { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? Details { get; private set; }

    private BillingAdjustmentEntity() { }

    public BillingAdjustmentEntity(Guid? groupId, Guid? actorUserId, BillingAdjustmentType type, int? days,
        string reason, string? details)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Informe o motivo.");
        GroupId = groupId;
        ActorUserId = actorUserId;
        Type = type;
        Days = days;
        Reason = reason.Trim()[..Math.Min(reason.Trim().Length, ReasonMaxLength)];
        Details = details is null ? null : details[..Math.Min(details.Length, 2000)];
    }
}

/// <summary>
/// Aviso recebido de uma loja (App Store Server Notifications V2 ou Google RTDN). Guardado
/// cru antes de processar: se o processamento falhar, aparece no painel de problemas e o
/// GodMode reprocessa.
/// </summary>
public sealed class StoreEventEntity : BaseEntity
{
    public const int RawMaxLength = 20000;

    public BillingStore Store { get; private set; }

    /// <summary>Apple: notificationUUID. Google: messageId do Pub/Sub. Único por loja.</summary>
    public string ExternalId { get; private set; } = null!;

    public string NotificationType { get; private set; } = null!;
    public string? Subtype { get; private set; }
    public string RawPayload { get; private set; } = null!;
    public StoreEventStatus Status { get; private set; }
    public string? Error { get; private set; }
    public Guid? GroupId { get; private set; }
    /// <summary>Apple: transactionId. Google: purchaseToken.</summary>
    public string? StoreReference { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }

    private StoreEventEntity() { }

    public StoreEventEntity(BillingStore store, string externalId, string notificationType, string? subtype, string rawPayload)
    {
        Store = store;
        ExternalId = externalId;
        NotificationType = notificationType;
        Subtype = subtype;
        RawPayload = rawPayload.Length > RawMaxLength ? rawPayload[..RawMaxLength] : rawPayload;
        Status = StoreEventStatus.Received;
    }

    public void Describe(string notificationType, string? subtype)
    {
        NotificationType = notificationType;
        Subtype = subtype;
    }

    public void MarkProcessed(Guid? groupId, string? storeReference, bool ignored = false)
    {
        Status = ignored ? StoreEventStatus.Ignored : StoreEventStatus.Processed;
        GroupId = groupId;
        StoreReference = storeReference;
        Error = null;
        ProcessedAt = DateTime.UtcNow;
        Attempts++;
    }

    public void MarkFailed(string error, string? storeReference)
    {
        Status = StoreEventStatus.Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
        StoreReference ??= storeReference;
        ProcessedAt = DateTime.UtcNow;
        Attempts++;
    }
}
