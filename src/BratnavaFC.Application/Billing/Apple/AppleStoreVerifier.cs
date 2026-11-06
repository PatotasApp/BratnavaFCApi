using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BratnavaFC.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Application.Billing.Apple;

/// <summary>
/// Confere compras da Apple pela App Store Server API: o app manda só o transactionId, e a
/// transação assinada vem da própria Apple (autenticada com a chave .p8), depois conferida
/// pelo <see cref="AppleJwsVerifier"/>. Tenta produção e, se a Apple não conhecer a
/// transação, o Sandbox (TestFlight e testadores).
/// </summary>
public sealed class AppleStoreVerifier : IStoreVerifier
{
    public const string HttpClientName = "AppStoreServerApi";
    private const string ProductionBase = "https://api.storekit.itunes.apple.com";
    private const string SandboxBase = "https://api.storekit-sandbox.itunes.apple.com";

    private readonly IHttpClientFactory _http;
    private readonly AppleBillingOptions _options;
    private readonly AppleJwsVerifier _jws;
    private readonly ILogger<AppleStoreVerifier> _logger;

    public AppleStoreVerifier(IHttpClientFactory http, IOptions<BillingOptions> options, ILogger<AppleStoreVerifier> logger)
    {
        _http = http;
        _options = options.Value.Apple;
        _jws = new AppleJwsVerifier(_options.TrustedRootSha256);
        _logger = logger;
    }

    public BillingStore Store => BillingStore.Apple;
    public bool IsAvailable => _options.IsConfigured;
    public AppleJwsVerifier Jws => _jws;

    public async Task<VerifiedStoreTransaction> VerifyAsync(StorePurchaseProof proof, CancellationToken ct)
    {
        if (!IsAvailable)
            throw new StoreVerificationException("Compra pela App Store ainda não está configurada no servidor.");
        if (string.IsNullOrWhiteSpace(proof.TransactionId))
            throw new StoreVerificationException("Faltou o transactionId da compra da Apple.");

        var transactionId = Uri.EscapeDataString(proof.TransactionId.Trim());

        var (signedTransaction, baseUrl) = await GetSignedTransactionAsync(transactionId, ct);
        var transaction = _jws.Verify(signedTransaction);

        // Status e renovação: a transação diz o período; a renovação diz se vai renovar.
        JsonElement? renewal = null;
        var state = StoreSubscriptionState.Active;
        try
        {
            using var statusDoc = await GetJsonAsync($"{baseUrl}/inApps/v1/subscriptions/{transactionId}", ct);
            foreach (var group in statusDoc.RootElement.GetProperty("data").EnumerateArray())
            foreach (var last in group.GetProperty("lastTransactions").EnumerateArray())
            {
                if (last.GetProperty("originalTransactionId").GetString() != Str(transaction, "originalTransactionId"))
                    continue;
                state = MapStatus(last.GetProperty("status").GetInt32());
                if (last.TryGetProperty("signedRenewalInfo", out var sri) && sri.GetString() is { } renewalJws)
                    renewal = _jws.Verify(renewalJws);
            }
        }
        catch (StoreVerificationException) { throw; }
        catch (Exception ex)
        {
            // Sem o status a compra ainda vale (a transação já foi conferida); só não sabemos
            // da renovação até o próximo aviso da Apple.
            _logger.LogWarning(ex, "[Billing/Apple] Não consegui ler o status da assinatura {TransactionId}.", proof.TransactionId);
        }

        return FromPayloads(transaction, renewal, state, _options.BundleId);
    }

    public Task AcknowledgeAsync(VerifiedStoreTransaction transaction, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Converte transação (e renovação) decodificadas no formato interno. Usado pela compra
    /// e pelos avisos V2 da Apple.
    /// </summary>
    public static VerifiedStoreTransaction FromPayloads(JsonElement transaction, JsonElement? renewal,
        StoreSubscriptionState? state, string? expectedBundleId)
    {
        var bundleId = Str(transaction, "bundleId");
        if (!string.IsNullOrWhiteSpace(expectedBundleId) && bundleId != expectedBundleId)
            throw new StoreVerificationException("Transação da Apple é de outro app.");

        var type = Str(transaction, "type");
        if (type is not null && type != "Auto-Renewable Subscription")
            throw new StoreVerificationException($"Produto da Apple de tipo inesperado ({type}).");

        var purchasedAt = Millis(transaction, "purchaseDate") ?? DateTime.UtcNow;
        var expiresAt = Millis(transaction, "expiresDate")
            ?? throw new StoreVerificationException("Transação da Apple sem data de vencimento.");
        var revokedAt = Millis(transaction, "revocationDate");

        var autoRenew = renewal is { } r && r.TryGetProperty("autoRenewStatus", out var ars) && ars.GetInt32() == 1;

        var finalState = state
            ?? (revokedAt != null ? StoreSubscriptionState.Revoked
                : expiresAt > DateTime.UtcNow ? StoreSubscriptionState.Active : StoreSubscriptionState.Expired);
        if (revokedAt != null) finalState = StoreSubscriptionState.Revoked;

        Guid? hint = Guid.TryParse(Str(transaction, "appAccountToken"), out var g) ? g : null;

        long? priceMicros = transaction.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt64() * 1000 // a Apple manda em milésimos da moeda
            : null;

        var env = Str(transaction, "environment") ?? "Production";

        return new VerifiedStoreTransaction(
            BillingStore.Apple,
            Str(transaction, "originalTransactionId") ?? throw new StoreVerificationException("Transação da Apple sem originalTransactionId."),
            Str(transaction, "transactionId") ?? throw new StoreVerificationException("Transação da Apple sem transactionId."),
            Str(transaction, "productId") ?? throw new StoreVerificationException("Transação da Apple sem produto."),
            purchasedAt,
            expiresAt,
            finalState,
            autoRenew && revokedAt == null,
            IsPending: false,
            revokedAt,
            revokedAt == null ? null : RevocationReason(transaction),
            hint,
            priceMicros,
            Str(transaction, "currency"),
            env,
            NeedsAcknowledge: false);
    }

    private async Task<(string Jws, string BaseUrl)> GetSignedTransactionAsync(string transactionId, CancellationToken ct)
    {
        var bases = _options.AllowSandbox ? new[] { ProductionBase, SandboxBase } : [ProductionBase];
        foreach (var baseUrl in bases)
        {
            var client = _http.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/inApps/v1/transactions/{transactionId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
            using var res = await client.SendAsync(req, ct);

            if (res.StatusCode == HttpStatusCode.NotFound)
                continue; // não existe neste ambiente — tenta o próximo

            await EnsureSuccessAsync(res, ct);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var jws = doc.RootElement.GetProperty("signedTransactionInfo").GetString()
                ?? throw new StoreVerificationException("Apple não devolveu a transação.");
            return (jws, baseUrl);
        }

        throw new StoreVerificationException("A Apple não encontrou esta compra.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        using var res = await client.SendAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct);
        var transient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests;
        throw new StoreVerificationException(
            $"App Store Server API respondeu {(int)res.StatusCode}: {Truncate(body, 300)}", transient);
    }

    /// <summary>JWT ES256 da App Store Server API (validade de 5 minutos).</summary>
    private string CreateToken()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = JsonSerializer.Serialize(new { alg = "ES256", kid = _options.KeyId, typ = "JWT" });
        var payload = JsonSerializer.Serialize(new
        {
            iss = _options.IssuerId,
            iat = now,
            exp = now + 300,
            aud = "appstoreconnect-v1",
            bid = _options.BundleId,
        });

        var unsigned = $"{AppleJwsVerifier.Base64UrlEncode(Encoding.UTF8.GetBytes(header))}.{AppleJwsVerifier.Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}";

        using var key = ECDsa.Create();
        key.ImportFromPem(_options.PrivateKey!.Replace("\\n", "\n"));
        var signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256);
        return $"{unsigned}.{AppleJwsVerifier.Base64UrlEncode(signature)}";
    }

    /// <summary>Status da App Store Server API: 1 ativa, 2 vencida, 3 nova tentativa, 4 carência, 5 revogada.</summary>
    public static StoreSubscriptionState MapStatus(int status) => status switch
    {
        1 => StoreSubscriptionState.Active,
        2 => StoreSubscriptionState.Expired,
        3 => StoreSubscriptionState.BillingRetry,
        4 => StoreSubscriptionState.GracePeriod,
        5 => StoreSubscriptionState.Revoked,
        _ => StoreSubscriptionState.Active,
    };

    private static string? RevocationReason(JsonElement t) =>
        t.TryGetProperty("revocationReason", out var r) && r.ValueKind == JsonValueKind.Number
            ? r.GetInt32() switch { 1 => "Reembolso (problema no app)", 0 => "Reembolso", _ => "Revogada" }
            : "Reembolso";

    internal static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static DateTime? Millis(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64()).UtcDateTime
            : null;

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}
