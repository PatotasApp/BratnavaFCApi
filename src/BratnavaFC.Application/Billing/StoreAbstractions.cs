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
/// Uma transação já conferida com a loja (nunca montada a partir do que o app diz).
/// </summary>
public sealed record VerifiedStoreTransaction(
    BillingStore Store,
    /// <summary>Apple: originalTransactionId. Google: purchaseToken.</summary>
    string StoreSubscriptionId,
    /// <summary>Apple: transactionId. Google: orderId da cobrança mais recente.</summary>
    string TransactionId,
    string ProductId,
    DateTime PurchasedAt,
    DateTime ExpiresAt,
    StoreSubscriptionState SubscriptionState,
    bool AutoRenewEnabled,
    bool IsPending,
    DateTime? RevokedAt,
    string? RevocationReason,
    /// <summary>Patota gravada na compra pela loja (appAccountToken / obfuscatedExternalAccountId).</summary>
    Guid? GroupHint,
    long? PriceMicros,
    string? Currency,
    string Environment,
    /// <summary>Google: ainda não confirmada (acknowledge). Apple: sempre falso.</summary>
    bool NeedsAcknowledge,
    /// <summary>Google: token da assinatura anterior que esta substituiu (troca de plano).</summary>
    string? LinkedStoreSubscriptionId = null);

/// <summary>Verificador de uma loja. Um por loja; o serviço escolhe pela <see cref="Store"/>.</summary>
public interface IStoreVerifier
{
    BillingStore Store { get; }

    /// <summary>Falso quando faltam credenciais: a compra é recusada com mensagem clara.</summary>
    bool IsAvailable { get; }

    Task<VerifiedStoreTransaction> VerifyAsync(StorePurchaseProof proof, CancellationToken ct);

    /// <summary>Google: confirma a compra (obrigatório em até 3 dias). Apple: nada a fazer.</summary>
    Task AcknowledgeAsync(VerifiedStoreTransaction transaction, CancellationToken ct);
}

/// <summary>Falha ao conferir a compra com a loja (compra inválida, de outro app, credencial errada).</summary>
public sealed class StoreVerificationException(string message, bool isTransient = false) : Exception(message)
{
    /// <summary>Verdadeiro quando vale tentar de novo (loja fora do ar, timeout).</summary>
    public bool IsTransient { get; } = isTransient;
}
