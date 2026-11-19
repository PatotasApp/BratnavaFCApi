using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Billing;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests.Billing;

public sealed class BillingAdminServiceTests
{
    [Fact]
    public async Task Estender_SomaDiasERegistraAuditoria()
    {
        var kit = new BillingTestKit(nameof(Estender_SomaDiasERegistraAuditoria));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);

        var result = await kit.Admin().ExtendAsync(g.Id, user.Id, new ExtendBillingDto(15, "Cortesia do lançamento"), default);

        result.Success.Should().BeTrue(result.Error);
        result.Data!.Summary.AccessEndsAt.Should().Be(kit.Now.AddDays(45));
        result.Data.Adjustments.Should().ContainSingle(a => a.Type == "ExtendDays" && a.Days == 15 && a.Reason == "Cortesia do lançamento");
    }

    [Fact]
    public async Task Estender_SemMotivo_Recusa()
    {
        var kit = new BillingTestKit(nameof(Estender_SemMotivo_Recusa));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);

        (await kit.Admin().ExtendAsync(g.Id, user.Id, new ExtendBillingDto(15, " "), default)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Liberar_DeixaVitalicia()
    {
        var kit = new BillingTestKit(nameof(Liberar_DeixaVitalicia));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-100));

        var result = await kit.Admin().SetLifetimeAsync(g.Id, user.Id, new SetLifetimeDto(true, "Parceiro"), default);

        result.Data!.Summary.Status.Should().Be("lifetime");
        result.Data.LifetimeReason.Should().Be("Parceiro");
    }

    [Fact]
    public async Task Configuracoes_ValidamEAuditam()
    {
        var kit = new BillingTestKit(nameof(Configuracoes_ValidamEAuditam));
        var user = kit.AddUser();
        var admin = kit.Admin();

        (await admin.UpdateSettingsAsync(user.Id, new UpdateBillingSettingsDto(-1, 3, false, false, "7", "x"), default)).Success.Should().BeFalse();

        var ok = await admin.UpdateSettingsAsync(user.Id, new UpdateBillingSettingsDto(14, 5, true, true, "10, 3, abc, 1", "Ajuste"), default);
        ok.Success.Should().BeTrue(ok.Error);
        ok.Data!.TrialDays.Should().Be(14);
        ok.Data.ReminderDays.Should().Be("10,3,1");
        ok.Data.EnforcementEnabled.Should().BeTrue();
        kit.Db.BillingAdjustments.Should().ContainSingle(a => a.Type == BillingAdjustmentType.SettingsChanged);
    }

    [Fact]
    public async Task Produto_AtivarNovoDesativaOAnteriorDaMesmaDuracao()
    {
        var kit = new BillingTestKit(nameof(Produto_AtivarNovoDesativaOAnteriorDaMesmaDuracao));
        var user = kit.AddUser();

        var result = await kit.Admin().UpsertProductAsync(user.Id, new UpsertBillingProductDto("google", "patota_1m_v2", 1, 34.90m, true, "Reajuste"), default);

        result.Success.Should().BeTrue(result.Error);
        var google1m = result.Data!.Products.Where(p => p.Store == "google" && p.Months == 1).ToList();
        google1m.Should().HaveCount(2);
        google1m.Single(p => p.IsActive).ProductId.Should().Be("patota_1m_v2");
        google1m.Single(p => p.IsActive).ReferencePrice.Should().Be(34.90m);
    }

    [Fact]
    public async Task Dashboard_ContaSituacoes()
    {
        var kit = new BillingTestKit(nameof(Dashboard_ContaSituacoes));
        var u1 = kit.AddUser("A");
        var u2 = kit.AddUser("B");
        var u3 = kit.AddUser("C");
        kit.AddGroup(u1.Id, kit.Now, "Em teste");
        kit.AddGroup(u2.Id, kit.Now.AddDays(-60), "Vencida");
        var paid = kit.AddGroup(u3.Id, kit.Now.AddDays(-31), "Paga");
        await kit.Billing().SubmitPurchaseAsync(paid.Id, u3.Id, new SubmitPurchaseDto("simulated", "patota_1m", null, null), null, default);

        var d = await kit.Admin().GetDashboardAsync(default);

        d.Total.Should().Be(3);
        d.Trial.Should().Be(1);
        d.Active.Should().Be(1);
        d.Expired.Should().Be(1);
        d.SimulatedPurchasesEnabled.Should().BeTrue();

        var expired = await kit.Admin().ListGroupsAsync("expired", null, default);
        expired.Should().NotContain(x => x.GroupName == "Paga");
        expired.Should().ContainSingle(x => x.GroupName == "Vencida");
    }

    [Fact]
    public async Task Estender_DuranteOTeste_EPacoteDepoisSomaNoFim()
    {
        var kit = new BillingTestKit(nameof(Estender_DuranteOTeste_EPacoteDepoisSomaNoFim));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        await kit.Admin().ExtendAsync(g.Id, user.Id, new ExtendBillingDto(10, "Cortesia"), default);

        kit.Now = kit.Now.AddDays(1);
        var paid = await kit.Billing().SubmitPurchaseAsync(g.Id, user.Id, new SubmitPurchaseDto("simulated", "patota_1m", null, null), null, default);

        paid.Data!.AccessEndsAt.Should().Be(kit.Now.AddDays(-1).AddDays(40).AddMonths(1));
    }

    [Fact]
    public async Task Simulacao_ReembolsoEDesfazer()
    {
        var kit = new BillingTestKit(nameof(Simulacao_ReembolsoEDesfazer));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-40));
        await kit.Billing().SubmitPurchaseAsync(g.Id, user.Id, new SubmitPurchaseDto("simulated", "patota_1m", null, null), null, default);
        var purchaseId = kit.Db.BillingPurchases.Single().Id;
        var admin = kit.Admin();

        var refunded = await admin.SimulateAsync(purchaseId, user.Id, new SimulatePurchaseEventDto("refund"), default);
        refunded.Data!.PaidUntil.Should().BeNull();
        refunded.Data.Summary.Status.Should().Be("expired");

        var back = await admin.SimulateAsync(purchaseId, user.Id, new SimulatePurchaseEventDto("unrefund"), default);
        back.Data!.PaidUntil.Should().Be(kit.Now.AddMonths(1));
    }
}

public sealed class BillingReminderJobTests
{
    private static (BillingReminderJob Job, Mock<IPushService> Push) Create(BillingTestKit kit)
    {
        var push = new Mock<IPushService>();
        return (new BillingReminderJob(kit.Db, kit.Billing(), push.Object, NullLogger<BillingReminderJob>.Instance, () => kit.Now), push);
    }

    [Fact]
    public async Task Desligado_NaoEnvia()
    {
        var kit = new BillingTestKit(nameof(Desligado_NaoEnvia));
        var user = kit.AddUser();
        kit.AddGroup(user.Id, kit.Now.AddDays(-23));
        var (job, push) = Create(kit);

        (await job.SendRemindersAsync(default)).Should().Be(0);
        push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SeteDiasAntes_AvisaAdminsEFinanceirosUmaVezSo()
    {
        var kit = new BillingTestKit(nameof(SeteDiasAntes_AvisaAdminsEFinanceirosUmaVezSo));
        kit.SetSettings(reminders: true);
        var admin = kit.AddUser("Admin");
        var fin = kit.AddUser("Fin");
        var g = kit.AddGroup(admin.Id, kit.Now.AddDays(-23));
        g.AddFinanceiro(fin.Id);
        await kit.Db.SaveChangesAsync();
        var (job, push) = Create(kit);

        (await job.SendRemindersAsync(default)).Should().Be(1);
        (await job.SendRemindersAsync(default)).Should().Be(0);

        push.Verify(p => p.SendToUsersAsync(
            It.Is<List<Guid>>(ids => ids.Count == 2 && ids.Contains(admin.Id) && ids.Contains(fin.Id)),
            "Plano vence em 7 dias", It.Is<string>(b => b.Contains("teste grátis")),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(), g.Id), Times.Once);
    }

    [Fact]
    public async Task DepoisDePagar_LembreteFalaDoPlano()
    {
        var kit = new BillingTestKit(nameof(DepoisDePagar_LembreteFalaDoPlano));
        kit.SetSettings(reminders: true);
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-60));
        kit.Now = kit.Now.AddMonths(-1).AddDays(3);
        await kit.Billing().SubmitPurchaseAsync(g.Id, user.Id, new SubmitPurchaseDto("simulated", "patota_1m", null, null), null, default);
        kit.Now = kit.Now.AddMonths(1).AddDays(-3); // faltam 3 dias para o fim do pacote
        var (job, push) = Create(kit);

        (await job.SendRemindersAsync(default)).Should().Be(1);
        push.Verify(p => p.SendToUsersAsync(It.IsAny<List<Guid>>(), "Plano vence em 3 dias", It.Is<string>(b => b.StartsWith("O plano")),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(), g.Id), Times.Once);
    }

    [Fact]
    public void Mensagens()
    {
        BillingReminderJob.Message("X", 0, new DateOnly(2026, 12, 6), false).Title.Should().Be("Plano vence hoje");
        BillingReminderJob.Message("X", 1, new DateOnly(2026, 12, 6), true).Body.Should().Contain("teste grátis").And.Contain("06/12");
    }
}
