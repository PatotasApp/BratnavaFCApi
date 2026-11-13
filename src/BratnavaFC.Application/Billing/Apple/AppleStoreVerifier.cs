using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BratnavaFC.Domain.Enums;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Application.Billing.Apple;

/// <summary>
/// Confere compras da Apple pela App Store Server API: o app manda só o transactionId, e a
/// transação assinada vem da própria Apple (autenticada com a chave .p8), depois conferida
/// pelo <see cref="AppleJwsVerifier"/>. Tenta produção e, se a Apple não conhecer a
/// transação, o Sandbox (TestFlight e testadores).
///
/// Os pacotes são "Non-Renewing Subscription" (assinatura sem renovação): a Apple não
/// controla vencimento, quem soma os meses é a nossa API.
/// </summary>
public sealed class AppleStoreVerifier : IStoreVerifier
{
    public const string HttpClientName = "AppStoreServerApi";
    public const string ExpectedType = "Non-Renewing Subscription";
    private const string ProductionBase = "https://api.storekit.itunes.apple.com";
    private const string SandboxBase = "https://api.storekit-sandbox.itunes.apple.com";

    private readonly IHttpClientFactory _http;
    private readonly AppleBillingOptions _options;
    private readonly AppleJwsVerifier _jws;

    public AppleStoreVerifier(IHttpClientFactory http, IOptions<BillingOptions> options)
    {
        _http = http;
        _options = options.Value.Apple;
        _jws = new AppleJwsVerifier(_options.TrustedRootSha256);
    }

    public BillingStore Store => BillingStore.Apple;
    public bool IsAvailable => _options.IsConfigured;

    public async Task<VerifiedStorePurchase> VerifyAsync(StorePurchaseProof proof, CancellationToken ct)
    {
        if (!IsAvailable)
            throw new StoreVerificationException("Compra pela App Store ainda não está configurada no servidor.");
        if (string.IsNullOrWhiteSpace(proof.TransactionId))
            throw new StoreVerificationException("Faltou o transactionId da compra da Apple.");

        var signed = await GetSignedTransactionAsync(Uri.EscapeDataString(proof.TransactionId.Trim()), ct);
        return FromPayload(_jws.Verify(signed), _options.BundleId);
    }

    public Task ConsumeAsync(string productId, string purchaseToken, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Converte a transação decodificada no formato interno. Usado pela compra e pelos avisos V2.</summary>
    public static VerifiedStorePurchase FromPayload(JsonElement transaction, string? expectedBundleId)
    {
        var bundleId = Str(transaction, "bundleId");
        if (!string.IsNullOrWhiteSpace(expectedBundleId) && bundleId != expectedBundleId)
            throw new StoreVerificationException("Transação da Apple é de outro app.");

        var type = Str(transaction, "type");
        if (type is not null && type != ExpectedType)
            throw new StoreVerificationException($"Produto da Apple de tipo inesperado ({type}); os pacotes devem ser \"{ExpectedType}\".");

        var revokedAt = Millis(transaction, "revocationDate");
        Guid? hint = Guid.TryParse(Str(transaction, "appAccountToken"), out var g) ? g : null;
        long? priceMicros = transaction.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt64() * 1000 // a Apple manda em milésimos da moeda
            : null;

        return new VerifiedStorePurchase(
            BillingStore.Apple,
            Str(transaction, "transactionId") ?? throw new StoreVerificationException("Transação da Apple sem transactionId."),
            Str(transaction, "productId") ?? throw new StoreVerificationException("Transação da Apple sem produto."),
            Millis(transaction, "purchaseDate") ?? DateTime.UtcNow,
            IsPending: false,
            revokedAt,
            revokedAt == null ? null : "Reembolso na App Store",
            hint,
            priceMicros,
            Str(transaction, "currency"),
            Str(transaction, "environment") ?? "Production",
            PurchaseToken: null,
            NeedsConsume: false);
    }

    private async Task<string> GetSignedTransactionAsync(string transactionId, CancellationToken ct)
    {
        var bases = _options.AllowSandbox ? new[] { ProductionBase, SandboxBase } : [ProductionBase];
        foreach (var baseUrl in bases)
        {
            var client = _http.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/inApps/v1/transactions/{transactionId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
            using var res = await client.SendAsync(req, ct);

            if (res.StatusCode == HttpStatusCode.NotFound)
                continue; // não existe neste ambiente: tenta o próximo

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var transient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests;
                throw new StoreVerificationException(
                    $"App Store Server API respondeu {(int)res.StatusCode}: {(body.Length > 300 ? body[..300] : body)}", transient);
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("signedTransactionInfo").GetString()
                ?? throw new StoreVerificationException("Apple não devolveu a transação.");
        }

        throw new StoreVerificationException("A Apple não encontrou esta compra.");
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

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static DateTime? Millis(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64()).UtcDateTime
            : null;
}
