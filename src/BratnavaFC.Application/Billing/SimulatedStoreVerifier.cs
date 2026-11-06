using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BratnavaFC.Application.Billing;

/// <summary>Onde a compra simulada pode existir.</summary>
public static class BillingEnvironment
{
    /// <summary>
    /// Só em Development e com <c>Billing:Simulated:Enabled = true</c>. As duas condições, de
    /// propósito: um appsettings copiado para produção com o flag ligado não abre a porta.
    /// </summary>
    public static bool SimulatedEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("Billing:Simulated:Enabled");
}

/// <summary>
/// "Loja" falsa para testar o fluxo inteiro sem App Store nem Google Play: aceita qualquer
/// compra de um produto cadastrado na loja Simulated e devolve uma assinatura de N meses a
/// partir de agora, com renovação automática ligada. As renovações, cancelamentos e
/// reembolsos simulados ficam no painel GodMode do site.
/// </summary>
public sealed class SimulatedStoreVerifier : IStoreVerifier
{
    private readonly AppDbContext _db;
    private readonly bool _enabled;
    private readonly Func<DateTime> _clock;

    public SimulatedStoreVerifier(AppDbContext db, IConfiguration configuration, IHostEnvironment environment)
        : this(db, configuration, environment, () => DateTime.UtcNow) { }

    internal SimulatedStoreVerifier(AppDbContext db, IConfiguration configuration, IHostEnvironment environment, Func<DateTime> clock)
    {
        _db = db;
        _enabled = BillingEnvironment.SimulatedEnabled(configuration, environment);
        _clock = clock;
    }

    public BillingStore Store => BillingStore.Simulated;
    public bool IsAvailable => _enabled;

    public async Task<VerifiedStoreTransaction> VerifyAsync(StorePurchaseProof proof, CancellationToken ct)
    {
        if (!_enabled)
            throw new StoreVerificationException("Compra simulada não está disponível neste ambiente.");

        var product = await _db.BillingProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Store == BillingStore.Simulated && p.ProductId == proof.ProductId, ct)
            ?? throw new StoreVerificationException("Produto simulado desconhecido.");

        var now = _clock();
        var transactionId = string.IsNullOrWhiteSpace(proof.TransactionId) ? $"sim-tx-{Guid.NewGuid():N}" : proof.TransactionId.Trim();
        var subscriptionId = string.IsNullOrWhiteSpace(proof.PurchaseToken) ? $"sim-sub-{Guid.NewGuid():N}" : proof.PurchaseToken.Trim();

        return new VerifiedStoreTransaction(
            BillingStore.Simulated,
            subscriptionId,
            transactionId,
            product.ProductId,
            now,
            now.AddMonths(product.Months),
            StoreSubscriptionState.Active,
            AutoRenewEnabled: true,
            IsPending: false,
            RevokedAt: null,
            RevocationReason: null,
            proof.GroupId,
            PriceMicros: product.ReferencePriceCents * 10_000L,
            Currency: "BRL",
            Environment: "Simulated",
            NeedsAcknowledge: false);
    }

    public Task AcknowledgeAsync(VerifiedStoreTransaction transaction, CancellationToken ct) => Task.CompletedTask;
}
