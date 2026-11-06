using BratnavaFC.Application.Billing;
using BratnavaFC.Domain.Entities.Billing;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests.Billing;

public sealed class BillingStatusCalculatorTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Teste_ContaDiasPeloCalendarioDeBrasilia()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 30, true);
        var snap = BillingStatusCalculator.Calculate(b, 3, false, Start);

        snap.Status.Should().Be(GroupBillingStatus.Trial);
        snap.AccessEndsOn.Should().Be(new DateOnly(2026, 12, 10));
        snap.DaysRemaining.Should().Be(30);
        snap.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void DepoisDeVencer_ToleranciaAntesDeVirarSomenteLeitura()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 30, true);

        BillingStatusCalculator.Calculate(b, 3, true, Start.AddDays(31)).Status.Should().Be(GroupBillingStatus.Grace);
        var expired = BillingStatusCalculator.Calculate(b, 3, true, Start.AddDays(33).AddMinutes(1));
        expired.Status.Should().Be(GroupBillingStatus.Expired);
        expired.IsReadOnly.Should().BeTrue();
        expired.DaysRemaining.Should().BeNegative();
    }

    [Fact]
    public void Vencida_ComInterruptorDesligado_NaoBloqueia()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 30, true);
        var snap = BillingStatusCalculator.Calculate(b, 3, false, Start.AddDays(60));

        snap.Status.Should().Be(GroupBillingStatus.Expired);
        snap.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void Vitalicio_NuncaVence()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 0, false);
        b.SetLifetime(true, "isenção");
        var snap = BillingStatusCalculator.Calculate(b, 0, true, Start.AddYears(5));

        snap.Status.Should().Be(GroupBillingStatus.Lifetime);
        snap.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void PeriodoPagoAlemDoTeste_FicaAtiva()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 30, true);
        b.SetPaidUntil(Start.AddDays(90));

        var snap = BillingStatusCalculator.Calculate(b, 3, true, Start.AddDays(40));
        snap.Status.Should().Be(GroupBillingStatus.Active);
        snap.AccessEndsAt.Should().Be(Start.AddDays(90));
    }

    [Fact]
    public void Estender_SomaAPartirDoVencimentoOuDeAgora()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 30, true);
        b.Extend(10, Start);
        b.AccessEndsAt.Should().Be(Start.AddDays(40));

        // Já vencida: soma a partir de agora.
        var later = Start.AddDays(100);
        b.Extend(5, later);
        b.AccessEndsAt.Should().Be(later.AddDays(5));
    }
}

public sealed class BillingServiceTests
{
    [Fact]
    public async Task PatotaNova_GanhaTesteDe30DiasDesdeACriacao()
    {
        var kit = new BillingTestKit(nameof(PatotaNova_GanhaTesteDe30DiasDesdeACriacao));
        var user = kit.AddUser();
        var created = kit.Now.AddDays(-2);
        var group = kit.AddGroup(user.Id, created);

        var billing = await kit.Billing().EnsureGroupAsync(group.Id, default);

        billing!.TrialGranted.Should().BeTrue();
        billing.TrialEndsAt.Should().Be(created.AddDays(30));
        kit.Db.BillingTrialGrants.Should().ContainSingle(t => t.UserId == user.Id && t.GroupId == group.Id);
    }

    [Fact]
    public async Task PatotaAnteriorAoLancamento_TesteComecaNoPrimeiroAcesso()
    {
        var kit = new BillingTestKit(nameof(PatotaAnteriorAoLancamento_TesteComecaNoPrimeiroAcesso));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, BillingTestKit.LaunchedAt.AddYears(-1));

        var billing = await kit.Billing().EnsureGroupAsync(group.Id, default);

        billing!.TrialStartedAt.Should().Be(kit.Now);
        billing.TrialEndsAt.Should().Be(kit.Now.AddDays(30));
    }

    [Fact]
    public async Task UmTesteGratisPorCriador_SegundaPatotaNasceSemTeste()
    {
        var kit = new BillingTestKit(nameof(UmTesteGratisPorCriador_SegundaPatotaNasceSemTeste));
        var user = kit.AddUser();
        var first = kit.AddGroup(user.Id, kit.Now.AddDays(-5), "Primeira");
        var second = kit.AddGroup(user.Id, kit.Now.AddDays(-1), "Segunda");
        var svc = kit.Billing();

        (await svc.EnsureGroupAsync(first.Id, default))!.TrialGranted.Should().BeTrue();
        var b2 = await svc.EnsureGroupAsync(second.Id, default);

        b2!.TrialGranted.Should().BeFalse();
        b2.AccessEndsAt.Should().Be(b2.TrialStartedAt);
    }

    [Fact]
    public async Task TesteSobreviveAExclusaoDaPatota()
    {
        var kit = new BillingTestKit(nameof(TesteSobreviveAExclusaoDaPatota));
        var user = kit.AddUser();
        var first = kit.AddGroup(user.Id, kit.Now.AddDays(-5), "Primeira");
        var svc = kit.Billing();
        await svc.EnsureGroupAsync(first.Id, default);

        kit.Db.GroupBillings.Remove(kit.Db.GroupBillings.Single());
        kit.Db.Groups.Remove(first);
        await kit.Db.SaveChangesAsync();

        var again = kit.AddGroup(user.Id, kit.Now, "Recriada");
        (await svc.EnsureGroupAsync(again.Id, default))!.TrialGranted.Should().BeFalse();
    }

    [Fact]
    public async Task Bratnava_EhVitalicia()
    {
        var kit = new BillingTestKit(nameof(Bratnava_EhVitalicia));
        var user = kit.AddUser();
        kit.AddGroup(user.Id, kit.Now.AddYears(-2), "Bratnava FC", BillingService.BratnavaGroupId);

        var status = await kit.Billing().GetStatusAsync(BillingService.BratnavaGroupId, user.Id, "android", default);

        status!.Status.Should().Be("lifetime");
        status.CanPurchase.Should().BeFalse();
        status.ShowBanner.Should().BeFalse();
        status.DaysRemaining.Should().BeNull();
    }

    [Fact]
    public async Task CompraSimulada_LiberaOsMesesEAparecNoHistorico()
    {
        var kit = new BillingTestKit(nameof(CompraSimulada_LiberaOsMesesEAparecNoHistorico));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now.AddDays(-40));
        var svc = kit.Billing();

        var result = await svc.SubmitPurchaseAsync(group.Id, user.Id, new SubmitPurchaseDto("simulated", "patota_3m", null, null), "android", default);

        result.Success.Should().BeTrue(result.Error);
        var status = result.Data!;
        status.Status.Should().Be("active");
        status.PaidUntil.Should().Be(kit.Now.AddMonths(3));
        status.History.Should().ContainSingle(h => h.Months == 3 && h.State == "confirmed" && h.PayerName == "Ana Teste");
        status.Subscription!.AutoRenewEnabled.Should().BeTrue();
        status.Subscription.IsMine.Should().BeTrue();
        // Já renova sozinha: não deixa comprar de novo (cobrança dupla).
        status.CanPurchase.Should().BeFalse();
        status.ShowBanner.Should().BeFalse();
    }

    [Fact]
    public async Task CompraSimulada_ForaDeDevelopment_Recusa()
    {
        var kit = new BillingTestKit(nameof(CompraSimulada_ForaDeDevelopment_Recusa), simulated: false);
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now);

        var result = await kit.Billing().SubmitPurchaseAsync(group.Id, user.Id, new SubmitPurchaseDto("simulated", "patota_1m", null, null), null, default);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(BillingCodes.StoreUnavailable);
        kit.Db.BillingPurchases.Should().BeEmpty();
    }

    [Fact]
    public void CompraSimulada_SoExisteEmDevelopment()
    {
        BillingEnvironment.SimulatedEnabled(BillingTestKit.Config(true), BillingTestKit.Env("Production")).Should().BeFalse();
        BillingEnvironment.SimulatedEnabled(BillingTestKit.Config(true), BillingTestKit.Env("Staging")).Should().BeFalse();
        BillingEnvironment.SimulatedEnabled(BillingTestKit.Config(false), BillingTestKit.Env("Development")).Should().BeFalse();
        BillingEnvironment.SimulatedEnabled(BillingTestKit.Config(true), BillingTestKit.Env("Development")).Should().BeTrue();
    }

    private static VerifiedStoreTransaction Tx(string txId, string subId, DateTime purchased, int months, Guid? hint,
        DateTime? revoked = null, string product = "patota_1m") =>
        new(BillingStore.Simulated, subId, txId, product, purchased, purchased.AddMonths(months),
            revoked == null ? StoreSubscriptionState.Active : StoreSubscriptionState.Revoked, revoked == null, false,
            revoked, revoked == null ? null : "Reembolso", hint, 29_900_000, "BRL", "Production", false);

    [Fact]
    public async Task MesmaTransacaoDuasVezes_NaoConcedeDuasVezes()
    {
        var kit = new BillingTestKit(nameof(MesmaTransacaoDuasVezes_NaoConcedeDuasVezes));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now);
        var svc = kit.Billing();
        var tx = Tx("tx-1", "sub-1", kit.Now, 1, group.Id);

        (await svc.ApplyVerifiedAsync(tx, group.Id, user.Id, default)).Success.Should().BeTrue();
        (await svc.ApplyVerifiedAsync(tx, null, null, default)).Success.Should().BeTrue();

        kit.Db.BillingPurchases.Should().ContainSingle();
        kit.Db.StoreSubscriptions.Should().ContainSingle();
        (await kit.Db.GroupBillings.SingleAsync()).PaidUntil.Should().Be(kit.Now.AddMonths(1));
    }

    [Fact]
    public async Task Renovacao_EstendeEReembolso_TiraOPeriodo()
    {
        var kit = new BillingTestKit(nameof(Renovacao_EstendeEReembolso_TiraOPeriodo));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now);
        var svc = kit.Billing();

        await svc.ApplyVerifiedAsync(Tx("tx-1", "sub-1", kit.Now, 1, group.Id), group.Id, user.Id, default);
        // A renovação chega pelo aviso da loja, sem patota no pedido: vem da assinatura.
        await svc.ApplyVerifiedAsync(Tx("tx-2", "sub-1", kit.Now.AddMonths(1), 1, null), null, null, default);
        (await kit.Db.GroupBillings.SingleAsync()).PaidUntil.Should().Be(kit.Now.AddMonths(2));

        await svc.ApplyVerifiedAsync(Tx("tx-2", "sub-1", kit.Now.AddMonths(1), 1, null, revoked: kit.Now.AddDays(35)), null, null, default);

        var billing = await kit.Db.GroupBillings.AsNoTracking().SingleAsync();
        billing.PaidUntil.Should().Be(kit.Now.AddMonths(1));
        kit.Db.BillingPurchases.Single(p => p.TransactionId == "tx-2").State.Should().Be(BillingPurchaseState.Refunded);
    }

    [Fact]
    public async Task AssinaturaDeOutraPatota_Recusa()
    {
        var kit = new BillingTestKit(nameof(AssinaturaDeOutraPatota_Recusa));
        var user = kit.AddUser();
        var a = kit.AddGroup(user.Id, kit.Now, "A");
        var b = kit.AddGroup(user.Id, kit.Now, "B");
        var svc = kit.Billing();

        await svc.ApplyVerifiedAsync(Tx("tx-1", "sub-1", kit.Now, 1, a.Id), a.Id, user.Id, default);
        var again = await svc.ApplyVerifiedAsync(Tx("tx-2", "sub-1", kit.Now, 1, a.Id), b.Id, user.Id, default);

        again.Success.Should().BeFalse();
        again.Code.Should().Be(BillingCodes.OtherGroup);
        again.Error.Should().Contain("A");
    }

    [Fact]
    public async Task CompraFeitaParaOutraPatota_Recusa()
    {
        var kit = new BillingTestKit(nameof(CompraFeitaParaOutraPatota_Recusa));
        var user = kit.AddUser();
        var a = kit.AddGroup(user.Id, kit.Now, "A");
        var b = kit.AddGroup(user.Id, kit.Now, "B");

        var result = await kit.Billing().ApplyVerifiedAsync(Tx("tx-1", "sub-1", kit.Now, 1, hint: a.Id), b.Id, user.Id, default);

        result.Code.Should().Be(BillingCodes.GroupMismatch);
        kit.Db.BillingPurchases.Should().BeEmpty();
    }

    [Fact]
    public async Task AvisoSemPatotaConhecida_Falha()
    {
        var kit = new BillingTestKit(nameof(AvisoSemPatotaConhecida_Falha));
        var result = await kit.Billing().ApplyVerifiedAsync(Tx("tx-1", "sub-x", kit.Now, 1, null), null, null, default);
        result.Code.Should().Be(BillingCodes.NoGroup);
    }

    [Fact]
    public async Task ProdutoDesconhecido_Recusa()
    {
        var kit = new BillingTestKit(nameof(ProdutoDesconhecido_Recusa));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);

        var result = await kit.Billing().ApplyVerifiedAsync(Tx("tx-1", "sub-1", kit.Now, 1, g.Id, product: "outro"), g.Id, user.Id, default);
        result.Code.Should().Be(BillingCodes.UnknownProduct);
    }

    [Fact]
    public async Task ContaQueJaPagaOutraPatota_NaoPodeComprarNaMesmaLoja()
    {
        var kit = new BillingTestKit(nameof(ContaQueJaPagaOutraPatota_NaoPodeComprarNaMesmaLoja));
        var user = kit.AddUser();
        var a = kit.AddGroup(user.Id, kit.Now, "Patota A");
        var b = kit.AddGroup(user.Id, kit.Now, "Patota B");
        var svc = kit.Billing();

        var google = new VerifiedStoreTransaction(BillingStore.Google, "token-a", "GPA.1", "patota_1m", kit.Now, kit.Now.AddMonths(1),
            StoreSubscriptionState.Active, true, false, null, null, a.Id, null, null, "Production", false);
        await svc.ApplyVerifiedAsync(google, a.Id, user.Id, default);

        var onAndroid = await svc.GetStatusAsync(b.Id, user.Id, "android", default);
        onAndroid!.CanPurchase.Should().BeFalse();
        onAndroid.PurchaseBlockedReason.Should().Contain("Patota A");

        // No iPhone a conta é outra (Apple), então pode.
        (await svc.GetStatusAsync(b.Id, user.Id, "ios", default))!.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public async Task Status_MostraSoOsProdutosDaPlataformaEOSimulado()
    {
        var kit = new BillingTestKit(nameof(Status_MostraSoOsProdutosDaPlataformaEOSimulado));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);

        var status = await kit.Billing().GetStatusAsync(g.Id, user.Id, "ios", default);

        status!.Products.Select(p => p.Store).Distinct().Should().BeEquivalentTo(["apple", "simulated"]);
        status.Products.Where(p => p.Store == "apple").Select(p => p.ReferencePrice).Should().Equal(29.90m, 84.90m, 322.90m);
        status.SimulatedPurchasesEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Faixa_ApareceComSeteDiasOuMenos()
    {
        var kit = new BillingTestKit(nameof(Faixa_ApareceComSeteDiasOuMenos));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-22));
        var svc = kit.Billing();

        var status = await svc.GetStatusAsync(g.Id, user.Id, "android", default);
        status!.DaysRemaining.Should().Be(8);
        status.ShowBanner.Should().BeFalse();

        kit.Now = kit.Now.AddDays(1);
        (await svc.GetStatusAsync(g.Id, user.Id, "android", default))!.ShowBanner.Should().BeTrue();
    }

    [Fact]
    public async Task Restaurar_LigaACompraAPatotaGravadaNaLoja()
    {
        var kit = new BillingTestKit(nameof(Restaurar_LigaACompraAPatotaGravadaNaLoja));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now, "Resenha");

        var fake = new FakeVerifier(BillingStore.Apple, new VerifiedStoreTransaction(
            BillingStore.Apple, "orig-1", "tx-1", "patota_1m", kit.Now, kit.Now.AddMonths(1), StoreSubscriptionState.Active,
            true, false, null, null, g.Id, 29_900_000, "BRL", "Sandbox", false));
        kit.Verifiers.Add(fake);

        var result = await kit.Billing().RestoreAsync(user.Id, new RestorePurchasesDto("apple", [new RestoreItemDto("patota_1m", "tx-1", null)]), default);

        result.Restored.Should().Be(1);
        result.Groups.Should().Equal("Resenha");
        (await kit.Db.GroupBillings.SingleAsync()).PaidUntil.Should().Be(kit.Now.AddMonths(1));
    }

    [Fact]
    public async Task Google_ConfirmaACompra()
    {
        var kit = new BillingTestKit(nameof(Google_ConfirmaACompra));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        var tx = new VerifiedStoreTransaction(BillingStore.Google, "token-1", "GPA.1", "patota_1m", kit.Now, kit.Now.AddMonths(1),
            StoreSubscriptionState.Active, true, false, null, null, g.Id, null, null, "Production", NeedsAcknowledge: true);
        var fake = new FakeVerifier(BillingStore.Google, tx);
        kit.Verifiers.Add(fake);

        var result = await kit.Billing().SubmitPurchaseAsync(g.Id, user.Id, new SubmitPurchaseDto("google", "patota_1m", null, "token-1"), "android", default);

        result.Success.Should().BeTrue(result.Error);
        fake.Acknowledged.Should().Be(1);
        kit.Db.StoreSubscriptions.Single().AcknowledgedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Google_RenovacaoComecaNoMesQueTerminaNoVencimento()
    {
        var kit = new BillingTestKit(nameof(Google_RenovacaoComecaNoMesQueTerminaNoVencimento));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        var svc = kit.Billing();
        var start = kit.Now;

        Task Apply(string order, DateTime expires) => svc.ApplyVerifiedAsync(new VerifiedStoreTransaction(
            BillingStore.Google, "token-1", order, "patota_1m", start, expires, StoreSubscriptionState.Active, true, false,
            null, null, g.Id, null, null, "Production", false), g.Id, user.Id, default);

        await Apply("GPA.1", start.AddMonths(1));
        await Apply("GPA.1..0", start.AddMonths(2));

        kit.Db.BillingPurchases.Single(p => p.TransactionId == "GPA.1..0").PurchasedAt.Should().Be(start.AddMonths(1));
    }

    internal sealed class FakeVerifier(BillingStore store, VerifiedStoreTransaction tx) : IStoreVerifier
    {
        public int Acknowledged { get; private set; }
        public BillingStore Store => store;
        public bool IsAvailable => true;
        public Task<VerifiedStoreTransaction> VerifyAsync(StorePurchaseProof proof, CancellationToken ct) => Task.FromResult(tx);
        public Task AcknowledgeAsync(VerifiedStoreTransaction transaction, CancellationToken ct)
        {
            Acknowledged++;
            return Task.CompletedTask;
        }
    }
}
