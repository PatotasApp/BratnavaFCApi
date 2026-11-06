using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BratnavaFC.Domain.Enums;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Application.Billing.Google;

/// <summary>
/// Confere assinaturas do Google Play pela Google Play Developer API
/// (<c>purchases.subscriptionsv2.get</c>) e confirma a compra (<c>acknowledge</c>), que o
/// Google exige em até 3 dias — sem isso a compra é reembolsada sozinha.
/// </summary>
public sealed class GooglePlayVerifier : IStoreVerifier
{
    public const string HttpClientName = "GooglePlayDeveloperApi";
    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";
    private const string BaseUrl = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly IHttpClientFactory _http;
    private readonly GoogleBillingOptions _options;
    private readonly Lazy<ITokenAccess?> _credential;

    public GooglePlayVerifier(IHttpClientFactory http, IOptions<BillingOptions> options)
    {
        _http = http;
        _options = options.Value.Google;
        _credential = new Lazy<ITokenAccess?>(LoadCredential);
    }

    public BillingStore Store => BillingStore.Google;
    public bool IsAvailable => _options.IsConfigured;

    public async Task<VerifiedStoreTransaction> VerifyAsync(StorePurchaseProof proof, CancellationToken ct)
    {
        if (!IsAvailable)
            throw new StoreVerificationException("Compra pelo Google Play ainda não está configurada no servidor.");
        if (string.IsNullOrWhiteSpace(proof.PurchaseToken))
            throw new StoreVerificationException("Faltou o purchaseToken da compra do Google Play.");

        var url = $"{BaseUrl}/{Uri.EscapeDataString(_options.PackageName!)}/purchases/subscriptionsv2/tokens/{Uri.EscapeDataString(proof.PurchaseToken.Trim())}";
        using var res = await SendAsync(HttpMethod.Get, url, null, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.BadRequest)
            throw new StoreVerificationException("O Google Play não reconheceu esta compra.");
        EnsureSuccess(res, body);

        using var doc = JsonDocument.Parse(body);
        return FromSubscriptionV2(doc.RootElement, proof.PurchaseToken.Trim(), proof.ProductId);
    }

    public async Task AcknowledgeAsync(VerifiedStoreTransaction transaction, CancellationToken ct)
    {
        if (!transaction.NeedsAcknowledge || !IsAvailable) return;

        var url = $"{BaseUrl}/{Uri.EscapeDataString(_options.PackageName!)}/purchases/subscriptions/{Uri.EscapeDataString(transaction.ProductId)}/tokens/{Uri.EscapeDataString(transaction.StoreSubscriptionId)}:acknowledge";
        using var res = await SendAsync(HttpMethod.Post, url, "{}", ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        EnsureSuccess(res, body);
    }

    /// <summary>Converte a resposta do subscriptionsv2 no formato interno. Público para teste.</summary>
    public static VerifiedStoreTransaction FromSubscriptionV2(JsonElement root, string purchaseToken, string? expectedProductId)
    {
        var lineItems = root.TryGetProperty("lineItems", out var li) && li.ValueKind == JsonValueKind.Array
            ? li.EnumerateArray().ToList()
            : [];
        if (lineItems.Count == 0)
            throw new StoreVerificationException("Assinatura do Google Play sem itens.");

        // Normalmente um item só. Se o app disse qual produto comprou, preferimos esse.
        var item = lineItems.FirstOrDefault(i => Str(i, "productId") == expectedProductId);
        if (item.ValueKind == JsonValueKind.Undefined) item = lineItems[0];

        var productId = Str(item, "productId") ?? throw new StoreVerificationException("Assinatura do Google Play sem produto.");
        var expiresAt = Time(item, "expiryTime") ?? throw new StoreVerificationException("Assinatura do Google Play sem vencimento.");
        var startedAt = Time(root, "startTime");

        var state = Str(root, "subscriptionState") ?? "";
        var mapped = state switch
        {
            "SUBSCRIPTION_STATE_ACTIVE" => StoreSubscriptionState.Active,
            "SUBSCRIPTION_STATE_CANCELED" => StoreSubscriptionState.Active, // cancelou a renovação, mas vale até vencer
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => StoreSubscriptionState.GracePeriod,
            "SUBSCRIPTION_STATE_ON_HOLD" => StoreSubscriptionState.BillingRetry,
            "SUBSCRIPTION_STATE_PAUSED" => StoreSubscriptionState.Paused,
            "SUBSCRIPTION_STATE_EXPIRED" => StoreSubscriptionState.Expired,
            "SUBSCRIPTION_STATE_PENDING" => StoreSubscriptionState.Pending,
            "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED" => StoreSubscriptionState.Expired,
            _ => StoreSubscriptionState.Active,
        };

        var autoRenew = item.TryGetProperty("autoRenewingPlan", out var plan)
                        && plan.TryGetProperty("autoRenewEnabled", out var are)
                        && are.ValueKind == JsonValueKind.True;

        var orderId = Str(item, "latestSuccessfulOrderId") ?? Str(root, "latestOrderId");
        var pending = mapped == StoreSubscriptionState.Pending;
        if (string.IsNullOrWhiteSpace(orderId))
        {
            if (!pending) throw new StoreVerificationException("Assinatura do Google Play sem número do pedido.");
            orderId = $"pending:{purchaseToken[..Math.Min(40, purchaseToken.Length)]}";
        }

        Guid? hint = null;
        if (root.TryGetProperty("externalAccountIdentifiers", out var ext))
        {
            if (Guid.TryParse(Str(ext, "obfuscatedExternalProfileId"), out var p)) hint = p;
            else if (Guid.TryParse(Str(ext, "obfuscatedExternalAccountId"), out var a)) hint = a;
        }

        long? priceMicros = null;
        string? currency = null;
        if (plan.ValueKind == JsonValueKind.Object && plan.TryGetProperty("recurringPrice", out var price))
        {
            currency = Str(price, "currencyCode");
            var units = long.TryParse(Str(price, "units"), out var u) ? u : 0;
            var nanos = price.TryGetProperty("nanos", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt64() : 0;
            priceMicros = units * 1_000_000 + nanos / 1000;
        }

        var acknowledged = Str(root, "acknowledgementState") == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED";
        var isTest = root.TryGetProperty("testPurchase", out _);

        return new VerifiedStoreTransaction(
            BillingStore.Google,
            purchaseToken,
            orderId!,
            productId,
            PurchasedAt: startedAt is { } s && s > expiresAt.AddYears(-2) ? (s > DateTime.UtcNow ? DateTime.UtcNow : s) : DateTime.UtcNow,
            expiresAt,
            mapped,
            autoRenew,
            pending,
            RevokedAt: null,
            RevocationReason: null,
            hint,
            priceMicros,
            currency,
            isTest ? "Sandbox" : "Production",
            NeedsAcknowledge: !acknowledged && !pending,
            LinkedStoreSubscriptionId: Str(root, "linkedPurchaseToken"));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? json, CancellationToken ct)
    {
        var credential = _credential.Value
            ?? throw new StoreVerificationException("Credencial do Google Play inválida no servidor.");
        string token;
        try
        {
            token = await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            throw new StoreVerificationException($"Não consegui autenticar no Google Play: {ex.Message}", isTransient: true);
        }

        var client = _http.CreateClient(HttpClientName);
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(req, ct);
    }

    private static void EnsureSuccess(HttpResponseMessage res, string body)
    {
        if (res.IsSuccessStatusCode) return;
        var transient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests;
        throw new StoreVerificationException(
            $"Google Play Developer API respondeu {(int)res.StatusCode}: {(body.Length > 300 ? body[..300] : body)}", transient);
    }

    private ITokenAccess? LoadCredential()
    {
        try
        {
            var cred = !string.IsNullOrWhiteSpace(_options.ServiceAccountJson)
                ? GoogleCredential.FromJson(_options.ServiceAccountJson)
                : GoogleCredential.FromFile(_options.ServiceAccountJsonPath!);
            return cred.CreateScoped(Scope);
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Time(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : null;
}
