using System.Text;
using System.Text.Json;
using BratnavaFC.Application.Billing.Apple;
using BratnavaFC.Domain.Entities.Billing;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Application.Billing;

/// <summary>Resultado de um aviso de loja, para o controller escolher o status HTTP.</summary>
public enum StoreNotificationOutcome
{
    /// <summary>Processado (ou já processado antes): 200.</summary>
    Ok,
    /// <summary>Aviso inválido (assinatura, formato): 400 — a loja não deve insistir.</summary>
    Rejected,
    /// <summary>Falha passageira ao processar: 500 — a loja manda de novo.</summary>
    RetryLater,
}

public interface IBillingNotificationService
{
    Task<StoreNotificationOutcome> HandleAppleAsync(string signedPayload, CancellationToken ct);
    Task<StoreNotificationOutcome> HandleGoogleAsync(string pubSubBody, CancellationToken ct);

    /// <summary>Reprocessa um aviso guardado (painel de problemas do GodMode).</summary>
    Task<StoreNotificationOutcome> RetryAsync(Guid eventId, CancellationToken ct);
}

/// <summary>
/// Avisos das lojas: App Store Server Notifications V2 (JWS assinado pela Apple) e Google
/// Real-Time Developer Notifications (via push do Pub/Sub). Todo aviso é guardado cru antes
/// de processar; o mesmo aviso repetido (a loja reenvia) não é aplicado duas vezes.
/// </summary>
public sealed class BillingNotificationService : IBillingNotificationService
{
    private readonly AppDbContext _db;
    private readonly IBillingService _billing;
    private readonly BillingOptions _options;
    private readonly ILogger<BillingNotificationService> _logger;
    private readonly AppleJwsVerifier _appleJws;

    public BillingNotificationService(AppDbContext db, IBillingService billing, IOptions<BillingOptions> options,
        ILogger<BillingNotificationService> logger)
        : this(db, billing, options, logger, new AppleJwsVerifier(options.Value.Apple.TrustedRootSha256)) { }

    internal BillingNotificationService(AppDbContext db, IBillingService billing, IOptions<BillingOptions> options,
        ILogger<BillingNotificationService> logger, AppleJwsVerifier appleJws)
    {
        _db = db;
        _billing = billing;
        _options = options.Value;
        _logger = logger;
        _appleJws = appleJws;
    }

    // ── Apple ───────────────────────────────────────────────────────────────────

    public async Task<StoreNotificationOutcome> HandleAppleAsync(string signedPayload, CancellationToken ct)
    {
        JsonElement payload;
        try
        {
            payload = _appleJws.Verify(signedPayload);
        }
        catch (StoreVerificationException ex)
        {
            var peek = AppleJwsVerifier.PeekPayload(signedPayload);
            var uuid = peek is { } p && AppleStoreVerifier.Str(p, "notificationUUID") is { } u ? $"invalid:{u}" : $"invalid:{Guid.NewGuid():N}";
            var ev = await RecordAsync(BillingStore.Apple, uuid, "INVALID_SIGNATURE", null, signedPayload, ct);
            ev.MarkFailed(ex.Message, null);
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("[Billing/Apple] Aviso com assinatura inválida: {Error}", ex.Message);
            return StoreNotificationOutcome.Rejected;
        }

        var notificationUuid = AppleStoreVerifier.Str(payload, "notificationUUID") ?? Guid.NewGuid().ToString();
        var type = AppleStoreVerifier.Str(payload, "notificationType") ?? "UNKNOWN";
        var subtype = AppleStoreVerifier.Str(payload, "subtype");

        var existing = await _db.StoreEvents.FirstOrDefaultAsync(e => e.Store == BillingStore.Apple && e.ExternalId == notificationUuid, ct);
        if (existing is { Status: StoreEventStatus.Processed or StoreEventStatus.Ignored })
            return StoreNotificationOutcome.Ok;

        var stored = existing ?? await RecordAsync(BillingStore.Apple, notificationUuid, type, subtype, signedPayload, ct);
        return await ProcessAppleAsync(stored, payload, ct);
    }

    private async Task<StoreNotificationOutcome> ProcessAppleAsync(StoreEventEntity ev, JsonElement payload, CancellationToken ct)
    {
        var type = AppleStoreVerifier.Str(payload, "notificationType") ?? "UNKNOWN";
        var subtype = AppleStoreVerifier.Str(payload, "subtype");
        ev.Describe(type, subtype);

        if (type == "TEST" || !payload.TryGetProperty("data", out var data))
        {
            ev.MarkProcessed(null, null, ignored: true);
            await _db.SaveChangesAsync(ct);
            return StoreNotificationOutcome.Ok;
        }

        string? originalTransactionId = null;
        try
        {
            if (AppleStoreVerifier.Str(data, "bundleId") is { } bundle && !string.IsNullOrWhiteSpace(_options.Apple.BundleId)
                && bundle != _options.Apple.BundleId)
                throw new StoreVerificationException("Aviso da Apple de outro app.");

            if (AppleStoreVerifier.Str(data, "signedTransactionInfo") is not { } signedTx)
            {
                ev.MarkProcessed(null, null, ignored: true);
                await _db.SaveChangesAsync(ct);
                return StoreNotificationOutcome.Ok;
            }

            var transaction = _appleJws.Verify(signedTx);
            originalTransactionId = AppleStoreVerifier.Str(transaction, "originalTransactionId");
            JsonElement? renewal = AppleStoreVerifier.Str(data, "signedRenewalInfo") is { } signedRenewal
                ? _appleJws.Verify(signedRenewal)
                : null;

            if (type == "REFUND_REVERSED")
            {
                var txId = AppleStoreVerifier.Str(transaction, "transactionId");
                var purchase = await _db.BillingPurchases.FirstOrDefaultAsync(p => p.Store == BillingStore.Apple && p.TransactionId == txId, ct);
                if (purchase != null)
                {
                    purchase.ReverseRefund();
                    await _db.SaveChangesAsync(ct);
                    await _billing.RecomputePaidUntilAsync(purchase.GroupId, ct);
                }
                ev.MarkProcessed(purchase?.GroupId, originalTransactionId, ignored: purchase == null);
                await _db.SaveChangesAsync(ct);
                return StoreNotificationOutcome.Ok;
            }

            StoreSubscriptionState? state = type switch
            {
                "EXPIRED" or "GRACE_PERIOD_EXPIRED" => StoreSubscriptionState.Expired,
                "DID_FAIL_TO_RENEW" => subtype == "GRACE_PERIOD" ? StoreSubscriptionState.GracePeriod : StoreSubscriptionState.BillingRetry,
                "REFUND" or "REVOKE" => StoreSubscriptionState.Revoked,
                "SUBSCRIBED" or "DID_RENEW" => StoreSubscriptionState.Active,
                _ => null,
            };

            var tx = AppleStoreVerifier.FromPayloads(transaction, renewal, state, _options.Apple.BundleId);
            var result = await _billing.ApplyVerifiedAsync(tx, null, null, ct);
            if (!result.Success)
                throw new StoreVerificationException(result.Error ?? "Falha ao aplicar o aviso.", result.Transient);

            ev.MarkProcessed(result.GroupId, originalTransactionId);
            await _db.SaveChangesAsync(ct);
            return StoreNotificationOutcome.Ok;
        }
        catch (Exception ex)
        {
            return await FailAsync(ev, ex, originalTransactionId, ct);
        }
    }

    // ── Google ──────────────────────────────────────────────────────────────────

    public async Task<StoreNotificationOutcome> HandleGoogleAsync(string pubSubBody, CancellationToken ct)
    {
        string messageId;
        JsonElement notification;
        try
        {
            using var doc = JsonDocument.Parse(pubSubBody);
            var message = doc.RootElement.GetProperty("message");
            messageId = message.TryGetProperty("messageId", out var mid) ? mid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
            var data = message.GetProperty("data").GetString() ?? "";
            notification = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(data))).RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Billing/Google] Push do Pub/Sub ilegível.");
            return StoreNotificationOutcome.Rejected;
        }

        var existing = await _db.StoreEvents.FirstOrDefaultAsync(e => e.Store == BillingStore.Google && e.ExternalId == messageId, ct);
        if (existing is { Status: StoreEventStatus.Processed or StoreEventStatus.Ignored })
            return StoreNotificationOutcome.Ok;

        var (type, subtype) = DescribeGoogle(notification);
        var stored = existing ?? await RecordAsync(BillingStore.Google, messageId, type, subtype, pubSubBody, ct);
        return await ProcessGoogleAsync(stored, notification, ct);
    }

    private async Task<StoreNotificationOutcome> ProcessGoogleAsync(StoreEventEntity ev, JsonElement n, CancellationToken ct)
    {
        string? token = null;
        try
        {
            if (n.TryGetProperty("packageName", out var pkg) && !string.IsNullOrWhiteSpace(_options.Google.PackageName)
                && pkg.GetString() != _options.Google.PackageName)
                throw new StoreVerificationException("Aviso do Google de outro app.");

            if (n.TryGetProperty("subscriptionNotification", out var sn))
            {
                token = sn.GetProperty("purchaseToken").GetString();
                var subscriptionId = sn.TryGetProperty("subscriptionId", out var sid) ? sid.GetString() : null;
                var verifier = _billing.GetVerifier(BillingStore.Google);
                if (verifier is not { IsAvailable: true })
                    throw new StoreVerificationException("Google Play não configurado no servidor.", isTransient: true);

                var tx = await verifier.VerifyAsync(new StorePurchaseProof(subscriptionId ?? "", null, token, null), ct);
                var result = await _billing.ApplyVerifiedAsync(tx, null, null, ct);
                if (!result.Success)
                    throw new StoreVerificationException(result.Error ?? "Falha ao aplicar o aviso.", result.Transient);

                ev.MarkProcessed(result.GroupId, token);
            }
            else if (n.TryGetProperty("voidedPurchaseNotification", out var vn))
            {
                token = vn.TryGetProperty("purchaseToken", out var t) ? t.GetString() : null;
                var orderId = vn.GetProperty("orderId").GetString() ?? "";
                var result = await _billing.RefundTransactionAsync(BillingStore.Google, orderId, "Reembolso no Google Play", DateTime.UtcNow, ct);
                if (!result.Success)
                    throw new StoreVerificationException($"Reembolso do pedido {orderId} sem cobrança correspondente.");
                ev.MarkProcessed(result.GroupId, token);
            }
            else
            {
                // testNotification, oneTimeProductNotification (não usamos) etc.
                ev.MarkProcessed(null, null, ignored: true);
            }

            await _db.SaveChangesAsync(ct);
            return StoreNotificationOutcome.Ok;
        }
        catch (Exception ex)
        {
            return await FailAsync(ev, ex, token, ct);
        }
    }

    private static (string Type, string? Subtype) DescribeGoogle(JsonElement n)
    {
        if (n.TryGetProperty("subscriptionNotification", out var sn))
        {
            var code = sn.TryGetProperty("notificationType", out var nt) && nt.ValueKind == JsonValueKind.Number ? nt.GetInt32() : 0;
            return ("SUBSCRIPTION", GoogleTypeName(code));
        }
        if (n.TryGetProperty("voidedPurchaseNotification", out _)) return ("VOIDED_PURCHASE", null);
        if (n.TryGetProperty("testNotification", out _)) return ("TEST", null);
        return ("OTHER", null);
    }

    /// <summary>Códigos do RTDN de assinatura.</summary>
    public static string GoogleTypeName(int code) => code switch
    {
        1 => "RECOVERED",
        2 => "RENEWED",
        3 => "CANCELED",
        4 => "PURCHASED",
        5 => "ON_HOLD",
        6 => "IN_GRACE_PERIOD",
        7 => "RESTARTED",
        8 => "PRICE_CHANGE_CONFIRMED",
        9 => "DEFERRED",
        10 => "PAUSED",
        11 => "PAUSE_SCHEDULE_CHANGED",
        12 => "REVOKED",
        13 => "EXPIRED",
        19 => "PRICE_CHANGE_UPDATED",
        20 => "PENDING_PURCHASE_CANCELED",
        _ => $"TYPE_{code}",
    };

    // ── Reprocessar ─────────────────────────────────────────────────────────────

    public async Task<StoreNotificationOutcome> RetryAsync(Guid eventId, CancellationToken ct)
    {
        var ev = await _db.StoreEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev == null) return StoreNotificationOutcome.Rejected;

        if (ev.Store == BillingStore.Apple)
        {
            JsonElement payload;
            try
            {
                payload = _appleJws.Verify(ev.RawPayload);
            }
            catch (StoreVerificationException ex)
            {
                ev.MarkFailed(ex.Message, null);
                await _db.SaveChangesAsync(ct);
                return StoreNotificationOutcome.Rejected;
            }
            return await ProcessAppleAsync(ev, payload, ct);
        }

        try
        {
            using var doc = JsonDocument.Parse(ev.RawPayload);
            var data = doc.RootElement.GetProperty("message").GetProperty("data").GetString() ?? "";
            var n = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(data))).RootElement.Clone();
            return await ProcessGoogleAsync(ev, n, ct);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
        {
            ev.MarkFailed("Aviso guardado ilegível.", null);
            await _db.SaveChangesAsync(ct);
            return StoreNotificationOutcome.Rejected;
        }
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────

    private async Task<StoreEventEntity> RecordAsync(BillingStore store, string externalId, string type, string? subtype, string raw, CancellationToken ct)
    {
        var ev = new StoreEventEntity(store, externalId, type, subtype, raw);
        _db.StoreEvents.Add(ev);
        await _db.SaveChangesAsync(ct);
        return ev;
    }

    private async Task<StoreNotificationOutcome> FailAsync(StoreEventEntity ev, Exception ex, string? storeSubscriptionId, CancellationToken ct)
    {
        var transient = ex is StoreVerificationException { IsTransient: true } || ex is HttpRequestException or TaskCanceledException;
        _logger.LogWarning(ex, "[Billing] Falha ao processar aviso {Store} {Type} {ExternalId}.", ev.Store, ev.NotificationType, ev.ExternalId);

        // O que estava pela metade no rastreador não pode ir junto com a marcação de falha.
        _db.ChangeTracker.Clear();
        var fresh = await _db.StoreEvents.FirstAsync(e => e.Id == ev.Id, ct);
        fresh.MarkFailed(ex.Message, storeSubscriptionId);
        await _db.SaveChangesAsync(ct);

        return transient ? StoreNotificationOutcome.RetryLater : StoreNotificationOutcome.Ok;
    }
}
