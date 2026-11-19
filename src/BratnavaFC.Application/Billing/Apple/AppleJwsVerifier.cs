using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace BratnavaFC.Application.Billing.Apple;

/// <summary>
/// Confere um JWS assinado pela Apple (transação ou notificação V2), do jeito da
/// biblioteca oficial da Apple:
/// <list type="number">
/// <item>o cabeçalho traz <c>alg = ES256</c> e a cadeia <c>x5c</c> (folha, intermediário, raiz);</item>
/// <item>a raiz da cadeia tem de ser uma das raízes fixadas (SHA-256 do "Apple Root CA - G3");</item>
/// <item>a cadeia fecha com essa raiz e a folha/intermediário têm os OIDs de compra no app da Apple;</item>
/// <item>a assinatura confere com a chave pública da folha.</item>
/// </list>
/// Qualquer falha lança <see cref="StoreVerificationException"/>.
/// </summary>
public sealed class AppleJwsVerifier
{
    /// <summary>OID que a Apple põe no certificado folha de assinatura de compra no app.</summary>
    public const string LeafOid = "1.2.840.113635.100.6.11.1";

    /// <summary>OID do certificado intermediário (Apple WWDR).</summary>
    public const string IntermediateOid = "1.2.840.113635.100.6.2.1";

    private readonly HashSet<string> _trustedRoots;
    private readonly Func<DateTime> _clock;

    public AppleJwsVerifier(IEnumerable<string> trustedRootSha256, Func<DateTime>? clock = null)
    {
        _trustedRoots = trustedRootSha256
            .Select(h => h.Replace(":", "").Replace(" ", "").Trim().ToLowerInvariant())
            .Where(h => h.Length > 0)
            .ToHashSet();
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>Devolve o payload já conferido.</summary>
    public JsonElement Verify(string jws)
    {
        if (string.IsNullOrWhiteSpace(jws))
            throw new StoreVerificationException("JWS da Apple vazio.");

        var parts = jws.Split('.');
        if (parts.Length != 3)
            throw new StoreVerificationException("JWS da Apple malformado.");

        JsonElement header;
        try
        {
            header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement.Clone();
        }
        catch (Exception ex) when (ex is not StoreVerificationException)
        {
            throw new StoreVerificationException("Cabeçalho do JWS da Apple ilegível.");
        }

        if (!header.TryGetProperty("alg", out var alg) || alg.GetString() != "ES256")
            throw new StoreVerificationException("JWS da Apple com algoritmo inesperado.");

        if (!header.TryGetProperty("x5c", out var x5c) || x5c.ValueKind != JsonValueKind.Array || x5c.GetArrayLength() < 3)
            throw new StoreVerificationException("JWS da Apple sem a cadeia de certificados.");

        var certs = x5c.EnumerateArray()
            .Select(e => X509CertificateLoader(e.GetString()))
            .ToList();

        try
        {
            var leaf = certs[0];
            var intermediate = certs[1];
            var root = certs[^1];

            var rootHash = Convert.ToHexString(SHA256.HashData(root.RawData)).ToLowerInvariant();
            if (!_trustedRoots.Contains(rootHash))
                throw new StoreVerificationException("JWS não foi assinado por uma raiz da Apple.");

            if (!HasExtension(leaf, LeafOid) || !HasExtension(intermediate, IntermediateOid))
                throw new StoreVerificationException("Certificados do JWS não são de compra no app da Apple.");

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ExtraStore.Add(intermediate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = _clock();

            if (!chain.Build(leaf))
            {
                var status = string.Join(", ", chain.ChainStatus.Select(s => s.Status));
                throw new StoreVerificationException($"Cadeia de certificados da Apple inválida ({status}).");
            }

            using var key = leaf.GetECDsaPublicKey()
                ?? throw new StoreVerificationException("Certificado da Apple sem chave EC.");

            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64UrlDecode(parts[2]);
            if (!key.VerifyData(signed, signature, HashAlgorithmName.SHA256))
                throw new StoreVerificationException("Assinatura do JWS da Apple não confere.");

            return JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement.Clone();
        }
        finally
        {
            foreach (var c in certs) c.Dispose();
        }
    }

    /// <summary>Lê o payload SEM conferir. Só para registrar um aviso que falhou na conferência.</summary>
    public static JsonElement? PeekPayload(string jws)
    {
        try
        {
            var parts = jws.Split('.');
            return parts.Length == 3 ? JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement.Clone() : null;
        }
        catch
        {
            return null;
        }
    }

    private static X509Certificate2 X509CertificateLoader(string? base64)
    {
        try
        {
            return new X509Certificate2(Convert.FromBase64String(base64 ?? ""));
        }
        catch
        {
            throw new StoreVerificationException("Certificado do JWS da Apple ilegível.");
        }
    }

    private static bool HasExtension(X509Certificate2 cert, string oid) =>
        cert.Extensions.Cast<X509Extension>().Any(e => e.Oid?.Value == oid);

    public static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
