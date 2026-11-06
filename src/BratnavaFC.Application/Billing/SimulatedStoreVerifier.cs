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
/// compra de um pacote cadastrado na loja Simulated. O reembolso simulado fica no painel
/// GodMode do site.
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

    public async Task<VerifiedStorePurchase> VerifyAsync(StorePurchaseProof proof, CancellationToken ct)
    {
        if (!_enabled)
            throw new StoreVerificationException("Compra simulada não está disponível neste ambiente.");

        var product = await _db.BillingProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Store == BillingStore.Simulated && p.ProductId == proof.ProductId, ct)
            ?? throw new StoreVerificationException("Produto simulado desconhecido.");

        var transactionId = string.IsNullOrWhiteSpace(proof.TransactionId) ? $"sim-{Guid.NewGuid():N}" : proof.TransactionId.Trim();

        return new VerifiedStorePurchase(
            BillingStore.Simulated,
            transactionId,
            product.ProductId,
            _clock(),
            IsPending: false,
            RevokedAt: null,
            RevocationReason: null,
            proof.GroupId,
            PriceMicros: product.ReferencePriceCents * 10_000L,
            Currency: "BRL",
            Environment: "Simulated",
            PurchaseToken: null,
            NeedsConsume: false);
    }

    public Task ConsumeAsync(string productId, string purchaseToken, CancellationToken ct) => Task.CompletedTask;
}
