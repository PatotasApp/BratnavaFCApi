using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BratnavaFC.Domain.Enums;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Application.Billing.Google;

/// <summary>
/// Confere compras do Google Play pela Google Play Developer API
/// (<c>purchases.products.get</c>) e consome a compra (<c>purchases.products.consume</c>).
/// Os pacotes são produtos avulsos consumíveis: consumir confirma a compra (o Google exige
/// em até 3 dias, senão reembolsa) e libera a mesma pessoa a comprar o pacote de novo.
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

    public async Task<VerifiedStorePurchase> VerifyAsync(StorePurchaseProof proof, CancellationToken ct)
    {
        if (!IsAvailable)
            throw new StoreVerificationException("Compra pelo Google Play ainda não está configurada no servidor.");
        if (string.IsNullOrWhiteSpace(proof.PurchaseToken))
            throw new StoreVerificationException("Faltou o purchaseToken da compra do Google Play.");
        if (string.IsNullOrWhiteSpace(proof.ProductId))
            throw new StoreVerificationException("Faltou o produto da compra do Google Play.");

        var token = proof.PurchaseToken.Trim();
        var url = $"{ProductUrl(proof.ProductId.Trim(), token)}";
        using var res = await SendAsync(HttpMethod.Get, url, null, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.BadRequest)
            throw new StoreVerificationException("O Google Play não reconheceu esta compra.");
        EnsureSuccess(res, body);

        using var doc = JsonDocument.Parse(body);
        return FromProductPurchase(doc.RootElement, proof.ProductId.Trim(), token);
    }

    public async Task ConsumeAsync(string productId, string purchaseToken, CancellationToken ct)
    {
        if (!IsAvailable) throw new StoreVerificationException("Google Play não configurado no servidor.", isTransient: true);
        using var res = await SendAsync(HttpMethod.Post, $"{ProductUrl(productId, purchaseToken)}:consume", "{}", ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        EnsureSuccess(res, body);
    }

    /// <summary>Converte a resposta do purchases.products.get no formato interno. Público para teste.</summary>
    public static VerifiedStorePurchase FromProductPurchase(JsonElement root, string productId, string purchaseToken)
    {
        // purchaseState: 0 comprado, 1 cancelado, 2 pendente.
        var state = Int(root, "purchaseState") ?? 0;
        var orderId = Str(root, "orderId");
        var pending = state == 2;
        if (string.IsNullOrWhiteSpace(orderId))
        {
            if (!pending) throw new StoreVerificationException("Compra do Google Play sem número do pedido.");
            orderId = $"pending:{purchaseToken[..Math.Min(40, purchaseToken.Length)]}";
        }

        var purchasedAt = long.TryParse(Str(root, "purchaseTimeMillis"), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : DateTime.UtcNow;

        Guid? hint = Guid.TryParse(Str(root, "obfuscatedExternalProfileId"), out var p) ? p
            : Guid.TryParse(Str(root, "obfuscatedExternalAccountId"), out var a) ? a : null;

        // consumptionState: 0 ainda não consumida, 1 consumida.
        var consumed = (Int(root, "consumptionState") ?? 0) == 1;
        // purchaseType: 0 = compra de teste (licença de teste).
        var isTest = Int(root, "purchaseType") == 0;

        return new VerifiedStorePurchase(
            BillingStore.Google,
            orderId!,
            Str(root, "productId") ?? productId,
            purchasedAt,
            pending,
            RevokedAt: state == 1 ? DateTime.UtcNow : null,
            RevocationReason: state == 1 ? "Compra cancelada no Google Play" : null,
            hint,
            PriceMicros: null, // o products.get não informa preço; vem do relatório da loja
            Currency: null,
            isTest ? "Sandbox" : "Production",
            purchaseToken,
            NeedsConsume: !consumed && !pending && state == 0);
    }

    private string ProductUrl(string productId, string token) =>
        $"{BaseUrl}/{Uri.EscapeDataString(_options.PackageName!)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(token)}";

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

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
