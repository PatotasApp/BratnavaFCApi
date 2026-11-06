using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Application.Billing;

/// <summary>O que o app manda depois que a loja conclui a compra.</summary>
public sealed record StorePurchaseProof(
    string ProductId,
    /// <summary>Apple: transactionId da compra (o app recebe do StoreKit).</summary>
    string? TransactionId,
    /// <summary>Google: purchaseToken.</summary>
    string? PurchaseToken,
    /// <summary>Patota para a qual o app comprou (vai também no appAccountToken / obfuscatedAccountId).</summary>
    Guid? GroupId);

/// <summary>
/// Uma compra de pacote já conferida com a loja (nunca montada a partir do que o app diz).
/// Apple: non-renewing subscription. Google: produto avulso consumível.
/// </summary>
public sealed record VerifiedStorePurchase(
    BillingStore Store,
    /// <summary>Apple: transactionId. Google: orderId. Único por loja.</summary>
    string TransactionId,
    string ProductId,
    DateTime PurchasedAt,
    bool IsPending,
    /// <summary>Reembolsada ou cancelada na loja.</summary>
    DateTime? RevokedAt,
    string? RevocationReason,
    /// <summary>Patota gravada na compra pela loja (appAccountToken / obfuscatedExternalAccountId).</summary>
    Guid? GroupHint,
    long? PriceMicros,
    string? Currency,
    string Environment,
    /// <summary>Google: purchaseToken.</summary>
    string? PurchaseToken,
    /// <summary>Google: ainda não consumida na loja.</summary>
    bool NeedsConsume);

/// <summary>Verificador de uma loja. Um por loja; o serviço escolhe pela <see cref="Store"/>.</summary>
public interface IStoreVerifier
{
    BillingStore Store { get; }

    /// <summary>Falso quando faltam credenciais: a compra é recusada com mensagem clara.</summary>
    bool IsAvailable { get; }

    Task<VerifiedStorePurchase> VerifyAsync(StorePurchaseProof proof, CancellationToken ct);

    /// <summary>Google: consome a compra (obrigatório em até 3 dias, e libera comprar o mesmo pacote de novo). Apple: nada.</summary>
    Task ConsumeAsync(string productId, string purchaseToken, CancellationToken ct);
}

/// <summary>Falha ao conferir a compra com a loja (compra inválida, de outro app, credencial errada).</summary>
public sealed class StoreVerificationException(string message, bool isTransient = false) : Exception(message)
{
    /// <summary>Verdadeiro quando vale tentar de novo (loja fora do ar, timeout).</summary>
    public bool IsTransient { get; } = isTransient;
}
