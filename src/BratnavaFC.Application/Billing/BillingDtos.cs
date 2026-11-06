namespace BratnavaFC.Application.Billing;

// ── App (admin e financeiro da patota) ─────────────────────────────────────────

/// <summary>Plano da patota, como o card do app mostra.</summary>
public sealed record GroupBillingStatusDto(
    Guid GroupId,
    /// <summary>"lifetime" | "trial" | "active" | "grace" | "expired".</summary>
    string Status,
    bool IsLifetime,
    DateTime AccessEndsAt,
    /// <summary>Último dia com acesso, no calendário de Brasília (yyyy-MM-dd). Nulo no vitalício.</summary>
    string? AccessEndsOn,
    DateTime GraceEndsAt,
    /// <summary>Dias até o vencimento (negativo depois). Nulo no vitalício.</summary>
    int? DaysRemaining,
    DateTime TrialEndsAt,
    bool TrialGranted,
    DateTime? PaidUntil,
    bool IsReadOnly,
    bool EnforcementEnabled,
    int GraceDays,
    /// <summary>Mostrar a faixa do Dashboard (faltam 7 dias ou menos e nada vai renovar sozinho).</summary>
    bool ShowBanner,
    BillingSubscriptionDto? Subscription,
    bool CanPurchase,
    string? PurchaseBlockedReason,
    IReadOnlyList<BillingProductDto> Products,
    bool SimulatedPurchasesEnabled,
    IReadOnlyList<BillingHistoryItemDto> History,
    string TermsUrl,
    string PrivacyUrl);

public sealed record BillingSubscriptionDto(
    Guid Id,
    string Store,
    string ProductId,
    int Months,
    bool AutoRenewEnabled,
    DateTime? ExpiresAt,
    string State,
    string? PayerName,
    bool IsMine);

public sealed record BillingProductDto(
    string Store,
    string ProductId,
    int Months,
    decimal ReferencePrice);

public sealed record BillingHistoryItemDto(
    Guid Id,
    DateTime PurchasedAt,
    DateTime ExpiresAt,
    int Months,
    string Store,
    string State,
    string? PayerName,
    decimal? Price,
    string? Currency);

/// <summary>Compra concluída na loja, mandada pelo app para o servidor conferir.</summary>
public sealed record SubmitPurchaseDto(
    /// <summary>"apple" | "google" | "simulated".</summary>
    string Store,
    string ProductId,
    string? TransactionId,
    string? PurchaseToken);

public sealed record RestorePurchasesDto(string Store, IReadOnlyList<RestoreItemDto> Items);

public sealed record RestoreItemDto(string ProductId, string? TransactionId, string? PurchaseToken);

public sealed record RestoreResultDto(int Restored, IReadOnlyList<string> Groups, IReadOnlyList<string> Errors);

/// <summary>Assinatura ativa do usuário (para o app avisar antes de comprar para outra patota).</summary>
public sealed record MySubscriptionDto(Guid GroupId, string GroupName, string Store, string ProductId, DateTime? ExpiresAt, bool AutoRenewEnabled);

// ── GodMode (site) ─────────────────────────────────────────────────────────────

public sealed record BillingDashboardDto(
    int Total,
    int Trial,
    int Active,
    int ExpiringSoon,
    int Grace,
    int Expired,
    int Lifetime,
    IReadOnlyList<BillingRevenueDto> Revenue,
    /// <summary>Patotas que já pagaram / patotas cujo teste acabou (inclui as que pagaram durante o teste).</summary>
    double ConversionRate,
    int ConvertedGroups,
    int TrialEndedGroups,
    int RefundsLast30Days,
    int OpenProblems,
    bool EnforcementEnabled,
    bool SimulatedPurchasesEnabled,
    IReadOnlyList<BillingStoreHealthDto> Stores);

public sealed record BillingRevenueDto(string Currency, decimal Last30Days, decimal Total, int PurchasesLast30Days);

public sealed record BillingStoreHealthDto(string Store, bool Configured);

public sealed record BillingGroupListItemDto(
    Guid GroupId,
    string GroupName,
    string? CreatorName,
    string Status,
    DateTime AccessEndsAt,
    string? AccessEndsOn,
    int? DaysRemaining,
    bool IsLifetime,
    string? Store,
    bool AutoRenewEnabled,
    int PurchaseCount,
    DateTime GroupCreatedAt);

public sealed record BillingGroupDetailDto(
    BillingGroupListItemDto Summary,
    DateTime TrialStartedAt,
    DateTime TrialEndsAt,
    bool TrialGranted,
    DateTime? PaidUntil,
    DateTime? CourtesyUntil,
    string? LifetimeReason,
    IReadOnlyList<AdminSubscriptionDto> Subscriptions,
    IReadOnlyList<AdminPurchaseDto> Purchases,
    IReadOnlyList<BillingAdjustmentDto> Adjustments,
    IReadOnlyList<StoreEventDto> Events);

public sealed record AdminSubscriptionDto(
    Guid Id, string Store, string StoreSubscriptionId, string ProductId, string State, bool AutoRenewEnabled,
    DateTime? ExpiresAt, string Environment, string? PayerName, DateTime? AcknowledgedAt, DateTime LastSyncedAt);

public sealed record AdminPurchaseDto(
    Guid Id, Guid GroupId, string? GroupName, string Store, string ProductId, string TransactionId, int Months,
    DateTime PurchasedAt, DateTime ExpiresAt, string State, decimal? Price, string? Currency, string Environment,
    string? PayerName, DateTime? RefundedAt, string? RefundReason);

public sealed record BillingAdjustmentDto(
    Guid Id, Guid? GroupId, string Type, int? Days, string Reason, string? Details, string? ActorName, DateTime CreatedAt);

public sealed record StoreEventDto(
    Guid Id, string Store, string ExternalId, string NotificationType, string? Subtype, string Status, string? Error,
    Guid? GroupId, string? StoreSubscriptionId, int Attempts, DateTime ReceivedAt, DateTime? ProcessedAt);

public sealed record BillingSettingsDto(
    int TrialDays,
    int GraceDays,
    bool EnforcementEnabled,
    bool RemindersEnabled,
    string ReminderDays,
    DateTime LaunchedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<AdminProductDto> Products,
    bool SimulatedPurchasesEnabled);

public sealed record AdminProductDto(Guid Id, string Store, string ProductId, int Months, decimal ReferencePrice, bool IsActive);

public sealed record UpdateBillingSettingsDto(
    int TrialDays, int GraceDays, bool EnforcementEnabled, bool RemindersEnabled, string ReminderDays, string Reason);

public sealed record UpsertBillingProductDto(string Store, string ProductId, int Months, decimal ReferencePrice, bool IsActive, string Reason);

public sealed record ExtendBillingDto(int Days, string Reason);

public sealed record SetLifetimeDto(bool Enabled, string Reason);

public sealed record ReprocessPurchaseDto(string Store, string ProductId, string? TransactionId, string? PurchaseToken, Guid? GroupId, string Reason);

public sealed record BillingProblemsDto(
    IReadOnlyList<AdminSubscriptionWithGroupDto> UnacknowledgedSubscriptions,
    IReadOnlyList<AdminPurchaseDto> PendingPurchases,
    IReadOnlyList<StoreEventDto> FailedEvents,
    IReadOnlyList<AdminPurchaseDto> RecentRefunds);

public sealed record AdminSubscriptionWithGroupDto(AdminSubscriptionDto Subscription, Guid GroupId, string? GroupName);

/// <summary>Ação simulada no painel (só Development): "renew" | "refund" | "cancel" | "resume" | "expire".</summary>
public sealed record SimulateSubscriptionEventDto(string Action);
