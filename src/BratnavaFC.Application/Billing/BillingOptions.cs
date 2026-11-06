namespace BratnavaFC.Application.Billing;

/// <summary>
/// Configuração da cobrança (seção <c>Billing</c>). Credenciais das lojas vêm de secret no
/// Fly (<c>Billing__Apple__PrivateKey</c> etc.); sem elas a loja fica "não configurada" e a
/// compra devolve erro claro em vez de liberar nada.
/// </summary>
public sealed class BillingOptions
{
    public const string Section = "Billing";

    public AppleBillingOptions Apple { get; set; } = new();
    public GoogleBillingOptions Google { get; set; } = new();
    public SimulatedBillingOptions Simulated { get; set; } = new();

    /// <summary>Links mostrados no card do plano (exigência das lojas junto da compra).</summary>
    public string TermsUrl { get; set; } = "https://patotasapp.com.br/termos";
    public string PrivacyUrl { get; set; } = "https://patotasapp.com.br/privacidade";
}

public sealed class AppleBillingOptions
{
    /// <summary>Bundle ID do app (ex.: br.com.patotasapp).</summary>
    public string? BundleId { get; set; }

    /// <summary>App Store Connect → Usuários e acesso → Integrações → Compras no app: Issuer ID.</summary>
    public string? IssuerId { get; set; }

    /// <summary>ID da chave de compras no app (.p8).</summary>
    public string? KeyId { get; set; }

    /// <summary>Conteúdo PEM da chave .p8 (-----BEGIN PRIVATE KEY----- ...).</summary>
    public string? PrivateKey { get; set; }

    /// <summary>
    /// SHA-256 (hex) dos certificados raiz aceitos na cadeia x5c dos JWS da Apple. O padrão é
    /// o "Apple Root CA - G3". Só mude se a Apple trocar a raiz.
    /// </summary>
    public string[] TrustedRootSha256 { get; set; } =
        ["63343abfb89a6a03ebb57e9b3f5fa7be7c4f5c756f3017b3a8c488c3653e9179"];

    /// <summary>Aceita transações do ambiente Sandbox (TestFlight / testadores). Desligar só se quiser.</summary>
    public bool AllowSandbox { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BundleId) && !string.IsNullOrWhiteSpace(IssuerId)
        && !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(PrivateKey);
}

public sealed class GoogleBillingOptions
{
    /// <summary>Nome do pacote do app (applicationId de produção).</summary>
    public string? PackageName { get; set; }

    /// <summary>Caminho do JSON da conta de serviço com acesso ao Play Console (financeiro).</summary>
    public string? ServiceAccountJsonPath { get; set; }

    /// <summary>O JSON da conta de serviço inteiro (alternativa ao caminho, para secret do Fly).</summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>
    /// Segredo do push do Pub/Sub: a URL cadastrada na assinatura do Pub/Sub termina em
    /// <c>?token=ESTE_VALOR</c>. Sem ele, o webhook do Google recusa tudo.
    /// </summary>
    public string? PubSubToken { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PackageName)
        && (!string.IsNullOrWhiteSpace(ServiceAccountJsonPath) || !string.IsNullOrWhiteSpace(ServiceAccountJson));
}

public sealed class SimulatedBillingOptions
{
    /// <summary>
    /// Liga a compra simulada. Só vale em Development: em qualquer outro ambiente a API
    /// ignora este valor (ver <c>BillingEnvironment.SimulatedEnabled</c>).
    /// </summary>
    public bool Enabled { get; set; }
}
