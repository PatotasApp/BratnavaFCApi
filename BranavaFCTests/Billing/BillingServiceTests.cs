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
    public void PacotePagoDepoisDoTeste_FicaPaga()
    {
        var b = new GroupBillingEntity(Guid.NewGuid(), Start, 30, true);
        b.SetTimeline(Start.AddDays(90), true);

        BillingStatusCalculator.Calculate(b, 3, true, Start.AddDays(10)).Status.Should().Be(GroupBillingStatus.Trial);
        var snap = BillingStatusCalculator.Calculate(b, 3, true, Start.AddDays(40));
        snap.Status.Should().Be(GroupBillingStatus.Active);
        snap.AccessEndsAt.Should().Be(Start.AddDays(90));
    }

    [Fact]
    public void Fila_SomaAPartirDoFimDoAnteriorOuDoMomento()
    {
        var trialEnd = Start.AddDays(30);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var (end, periods) = BillingTimeline.Compute(trialEnd, [
            new BillingTimelineItem(a, Start.AddDays(5), 1, 0, true),      // pago no teste: começa no fim do teste
            new BillingTimelineItem(b, Start.AddDays(6), 3, 0, true),      // pago antes de vencer: soma ao fim do anterior
            new BillingTimelineItem(c, Start.AddYears(1), 0, 10, false),   // dado com a patota já vencida: começa no momento
        ]);

        periods[0].Start.Should().Be(trialEnd);
        periods[1].Start.Should().Be(trialEnd.AddMonths(1));
        periods[2].Start.Should().Be(Start.AddYears(1));
        end.Should().Be(Start.AddYears(1).AddDays(10));
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

    private static SubmitPurchaseDto Sim(string product) => new("simulated", product, null, null);

    [Fact]
    public async Task PagarDuranteOTeste_SomaDepoisDoTeste()
    {
        var kit = new BillingTestKit(nameof(PagarDuranteOTeste_SomaDepoisDoTeste));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now.AddDays(-10));
        var trialEnd = kit.Now.AddDays(20);

        var result = await kit.Billing().SubmitPurchaseAsync(group.Id, user.Id, Sim("patota_3m"), "android", default);

        result.Success.Should().BeTrue(result.Error);
        var status = result.Data!;
        status.Status.Should().Be("trial");
        status.PaidUntil.Should().Be(trialEnd.AddMonths(3));
        status.AccessEndsAt.Should().Be(trialEnd.AddMonths(3));
        status.History.Should().ContainSingle(h => h.Months == 3 && h.State == "confirmed" && h.PayerName == "Ana Teste" && h.PeriodStart == trialEnd);
        // Pacote avulso: pode pagar de novo, antes de vencer, quantas vezes quiser.
        status.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public async Task PagarAntesDeVencer_SomaAoVencimentoAtual_OutroAdminTambemPaga()
    {
        var kit = new BillingTestKit(nameof(PagarAntesDeVencer_SomaAoVencimentoAtual_OutroAdminTambemPaga));
        var ana = kit.AddUser("Ana");
        var bia = kit.AddUser("Bia");
        var group = kit.AddGroup(ana.Id, kit.Now.AddDays(-40));
        var svc = kit.Billing();

        await svc.SubmitPurchaseAsync(group.Id, ana.Id, Sim("patota_1m"), null, default);
        kit.Now = kit.Now.AddDays(10);
        var second = await svc.SubmitPurchaseAsync(group.Id, bia.Id, Sim("patota_12m"), null, default);

        // Venceu o teste 10 dias antes da 1ª compra: começa na compra.
        var firstStart = kit.Now.AddDays(-10);
        second.Data!.Status.Should().Be("active");
        second.Data.PaidUntil.Should().Be(firstStart.AddMonths(1).AddMonths(12));
        second.Data.History.Select(h => h.PayerName).Should().BeEquivalentTo(["Ana Teste", "Bia Teste"]);
    }

    [Fact]
    public async Task CompraSimulada_ForaDeDevelopment_Recusa()
    {
        var kit = new BillingTestKit(nameof(CompraSimulada_ForaDeDevelopment_Recusa), simulated: false);
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now);

        var result = await kit.Billing().SubmitPurchaseAsync(group.Id, user.Id, Sim("patota_1m"), null, default);

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

    internal static VerifiedStorePurchase Tx(string txId, DateTime purchased, Guid? hint, DateTime? revoked = null,
        string product = "patota_1m", BillingStore store = BillingStore.Simulated, string? token = null, bool needsConsume = false) =>
        new(store, txId, product, purchased, false, revoked, revoked == null ? null : "Reembolso", hint,
            29_900_000, "BRL", "Production", token, needsConsume);

    [Fact]
    public async Task MesmaCompraDuasVezes_NaoSomaDuasVezes()
    {
        var kit = new BillingTestKit(nameof(MesmaCompraDuasVezes_NaoSomaDuasVezes));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now.AddDays(-40));
        var svc = kit.Billing();
        var tx = Tx("tx-1", kit.Now, group.Id);

        (await svc.ApplyVerifiedAsync(tx, group.Id, user.Id, default)).Success.Should().BeTrue();
        (await svc.ApplyVerifiedAsync(tx, null, null, default)).Success.Should().BeTrue();

        kit.Db.BillingPurchases.Should().ContainSingle();
        (await kit.Db.GroupBillings.SingleAsync()).PaidUntil.Should().Be(kit.Now.AddMonths(1));
    }

    [Fact]
    public async Task Reembolso_TiraOsMesesDaquelePacote()
    {
        var kit = new BillingTestKit(nameof(Reembolso_TiraOsMesesDaquelePacote));
        var user = kit.AddUser();
        var group = kit.AddGroup(user.Id, kit.Now.AddDays(-40));
        var svc = kit.Billing();

        await svc.ApplyVerifiedAsync(Tx("tx-1", kit.Now, group.Id), group.Id, user.Id, default);
        await svc.ApplyVerifiedAsync(Tx("tx-2", kit.Now.AddMinutes(5), group.Id, product: "patota_3m"), group.Id, user.Id, default);
        (await kit.Db.GroupBillings.SingleAsync()).PaidUntil.Should().Be(kit.Now.AddMonths(4));

        // A Apple/Google avisam o reembolso do 1º pacote: o de 3 meses passa a começar na compra dele.
        await svc.ApplyVerifiedAsync(Tx("tx-1", kit.Now, group.Id, revoked: kit.Now.AddDays(3)), null, null, default);

        var billing = await kit.Db.GroupBillings.AsNoTracking().SingleAsync();
        billing.PaidUntil.Should().Be(kit.Now.AddMinutes(5).AddMonths(3));
        kit.Db.BillingPurchases.Single(p => p.TransactionId == "tx-1").State.Should().Be(BillingPurchaseState.Refunded);
        kit.Db.BillingPurchases.Single(p => p.TransactionId == "tx-1").PeriodEnd.Should().BeNull();
    }

    [Fact]
    public async Task MesmaCompraEmOutraPatota_Recusa()
    {
        var kit = new BillingTestKit(nameof(MesmaCompraEmOutraPatota_Recusa));
        var user = kit.AddUser();
        var a = kit.AddGroup(user.Id, kit.Now, "A");
        var b = kit.AddGroup(user.Id, kit.Now, "B");
        var svc = kit.Billing();

        await svc.ApplyVerifiedAsync(Tx("tx-1", kit.Now, null), a.Id, user.Id, default);
        var again = await svc.ApplyVerifiedAsync(Tx("tx-1", kit.Now, null), b.Id, user.Id, default);

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

        var result = await kit.Billing().ApplyVerifiedAsync(Tx("tx-1", kit.Now, hint: a.Id), b.Id, user.Id, default);

        result.Code.Should().Be(BillingCodes.GroupMismatch);
        kit.Db.BillingPurchases.Should().BeEmpty();
    }

    [Fact]
    public async Task AvisoSemPatotaConhecida_Falha()
    {
        var kit = new BillingTestKit(nameof(AvisoSemPatotaConhecida_Falha));
        var result = await kit.Billing().ApplyVerifiedAsync(Tx("tx-1", kit.Now, null), null, null, default);
        result.Code.Should().Be(BillingCodes.NoGroup);
    }

    [Fact]
    public async Task ProdutoDesconhecido_Recusa()
    {
        var kit = new BillingTestKit(nameof(ProdutoDesconhecido_Recusa));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);

        var result = await kit.Billing().ApplyVerifiedAsync(Tx("tx-1", kit.Now, g.Id, product: "outro"), g.Id, user.Id, default);
        result.Code.Should().Be(BillingCodes.UnknownProduct);
    }

    [Fact]
    public async Task Status_MostraOsPacotesDaPlataformaEAteQuandoVaiCadaUm()
    {
        var kit = new BillingTestKit(nameof(Status_MostraOsPacotesDaPlataformaEAteQuandoVaiCadaUm));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now); // teste até 10/12/2026

        var status = await kit.Billing().GetStatusAsync(g.Id, user.Id, "ios", default);

        status!.Products.Select(p => p.Store).Distinct().Should().BeEquivalentTo(["apple", "simulated"]);
        var apple = status.Products.Where(p => p.Store == "apple").ToList();
        apple.Select(p => p.ReferencePrice).Should().Equal(29.90m, 84.90m, 322.90m);
        apple[0].ExtendsUntilOn.Should().Be("2027-01-10");
        apple[2].ExtendsUntilOn.Should().Be("2027-12-10");
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
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-40), "Resenha");
        kit.Verifiers.Add(new FakeVerifier(BillingStore.Apple, Tx("tx-1", kit.Now, g.Id, store: BillingStore.Apple)));

        var result = await kit.Billing().RestoreAsync(user.Id, new RestorePurchasesDto("apple", [new RestoreItemDto("patota_1m", "tx-1", null)]), default);

        result.Restored.Should().Be(1);
        result.Groups.Should().Equal("Resenha");
        (await kit.Db.GroupBillings.SingleAsync()).PaidUntil.Should().Be(kit.Now.AddMonths(1));
    }

    [Fact]
    public async Task Google_ConsomeACompra()
    {
        var kit = new BillingTestKit(nameof(Google_ConsomeACompra));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        var fake = new FakeVerifier(BillingStore.Google,
            Tx("GPA.1", kit.Now, g.Id, store: BillingStore.Google, token: "token-1", needsConsume: true));
        kit.Verifiers.Add(fake);

        var result = await kit.Billing().SubmitPurchaseAsync(g.Id, user.Id, new SubmitPurchaseDto("google", "patota_1m", null, "token-1"), "android", default);

        result.Success.Should().BeTrue(result.Error);
        fake.Consumed.Should().Be(1);
        kit.Db.BillingPurchases.Single().ConsumedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Google_FalhaAoConsumir_FicaNosProblemasEJobTentaDeNovo()
    {
        var kit = new BillingTestKit(nameof(Google_FalhaAoConsumir_FicaNosProblemasEJobTentaDeNovo));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        var fake = new FakeVerifier(BillingStore.Google,
            Tx("GPA.1", kit.Now, g.Id, store: BillingStore.Google, token: "token-1", needsConsume: true)) { FailConsume = true };
        kit.Verifiers.Add(fake);
        var svc = kit.Billing();

        (await svc.SubmitPurchaseAsync(g.Id, user.Id, new SubmitPurchaseDto("google", "patota_1m", null, "token-1"), "android", default))
            .Success.Should().BeTrue();
        (await kit.Admin().GetProblemsAsync(default)).UnconsumedPurchases.Should().ContainSingle();

        fake.FailConsume = false;
        (await svc.ConsumePendingAsync(default)).Should().Be(1);
        (await kit.Admin().GetProblemsAsync(default)).UnconsumedPurchases.Should().BeEmpty();
    }

    internal sealed class FakeVerifier(BillingStore store, VerifiedStorePurchase tx) : IStoreVerifier
    {
        public int Consumed { get; private set; }
        public bool FailConsume { get; set; }
        public BillingStore Store => store;
        public bool IsAvailable => true;
        public Task<VerifiedStorePurchase> VerifyAsync(StorePurchaseProof proof, CancellationToken ct) => Task.FromResult(tx);
        public Task ConsumeAsync(string productId, string purchaseToken, CancellationToken ct)
        {
            if (FailConsume) throw new StoreVerificationException("fora do ar", true);
            Consumed++;
            return Task.CompletedTask;
        }
    }
}
