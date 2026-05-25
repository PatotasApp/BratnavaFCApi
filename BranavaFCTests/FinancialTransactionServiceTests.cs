using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

public class FinancialTransactionServiceTests
{
    private static FinancialTransactionService MakeSut(
        BratnavaFC.Infrastructure.Data.AppDbContext db) => new(db);

    // ── RecordOrRemovePaymentEntryAsync — guarda zero-amount ─────────────────

    /// <summary>Regression test: pagamento com desconto total (amount = 0) não deve criar lançamento.</summary>
    [Fact]
    public async Task RecordOrRemove_ZeroAmount_ShouldNotCreateTransaction()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_ZeroAmount_ShouldNotCreateTransaction));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        // Act — should NOT throw (was the bug before the fix)
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            amount: 0m, "Mensalidade Maio/2026 – João",
            new DateOnly(2026, 5, 1), isPaid: true, "João");

        // Assert — nothing created
        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordOrRemove_NegativeAmount_ShouldNotCreateTransaction()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_NegativeAmount_ShouldNotCreateTransaction));
        var sut = MakeSut(db);

        await sut.RecordOrRemovePaymentEntryAsync(
            Guid.NewGuid(), TransactionSourceType.ExtraCharge, Guid.NewGuid(),
            amount: -10m, "Cobrança – Maria",
            new DateOnly(2026, 5, 1), isPaid: true, "Maria");

        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    // ── RecordOrRemovePaymentEntryAsync — criação ─────────────────────────────

    [Fact]
    public async Task RecordOrRemove_PositiveAmount_ShouldCreateTransaction()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_PositiveAmount_ShouldCreateTransaction));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "Mensalidade Maio/2026 – João",
            new DateOnly(2026, 5, 1), isPaid: true, "João");

        var tx = await db.GroupTransactions.SingleAsync();
        tx.GroupId.Should().Be(groupId);
        tx.Amount.Should().Be(100m);
        tx.IsAutomatic.Should().BeTrue();
        tx.Type.Should().Be(TransactionType.Income);
        tx.SourceType.Should().Be(TransactionSourceType.MonthlyPayment);
        tx.SourceId.Should().Be(sourceId);
        tx.PlayerName.Should().Be("João");
    }

    [Fact]
    public async Task RecordOrRemove_CalledTwiceForSameSource_ShouldNotDuplicate()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_CalledTwiceForSameSource_ShouldNotDuplicate));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "desc", new DateOnly(2026, 5, 1), isPaid: true, null);

        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "desc", new DateOnly(2026, 5, 1), isPaid: true, null);

        (await db.GroupTransactions.CountAsync()).Should().Be(1, "idempotente — não deve criar duplicata");
    }

    // ── RecordOrRemovePaymentEntryAsync — remoção ─────────────────────────────

    [Fact]
    public async Task RecordOrRemove_UnpayExistingEntry_ShouldRemoveIt()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_UnpayExistingEntry_ShouldRemoveIt));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        // Cria a entrada
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "desc", new DateOnly(2026, 5, 1), isPaid: true, null);

        (await db.GroupTransactions.CountAsync()).Should().Be(1);

        // Reverte para pendente → deve remover
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "desc", new DateOnly(2026, 5, 1), isPaid: false, null);

        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordOrRemove_UnpayWhenNoEntry_ShouldBeNoOp()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_UnpayWhenNoEntry_ShouldBeNoOp));
        var sut = MakeSut(db);

        var act = async () => await sut.RecordOrRemovePaymentEntryAsync(
            Guid.NewGuid(), TransactionSourceType.MonthlyPayment, Guid.NewGuid(),
            100m, "desc", new DateOnly(2026, 5, 1), isPaid: false, null);

        await act.Should().NotThrowAsync();
        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    // ── GetByMonthAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByMonth_ShouldReturnOnlyTransactionsForThatMonth()
    {
        await using var db = DbContextFactory.Create(nameof(GetByMonth_ShouldReturnOnlyTransactionsForThatMonth));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        db.GroupTransactions.AddRange(
            new GroupTransactionEntity(groupId, TransactionType.Income, 100m, "Maio", new DateOnly(2026, 5, 10), userId),
            new GroupTransactionEntity(groupId, TransactionType.Income, 200m, "Junho", new DateOnly(2026, 6, 1), userId));
        await db.SaveChangesAsync();

        var result = await sut.GetByMonthAsync(groupId, 2026, 5);

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(1);
        result.Data![0].Description.Should().Be("Maio");
    }

    // ── GetMonthlySummariesAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetMonthlySummaries_ShouldComputeAccumulatedBalance()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlySummaries_ShouldComputeAccumulatedBalance));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        db.GroupTransactions.AddRange(
            // Maio: +300 entrada, -100 saída → saldo = +200
            new GroupTransactionEntity(groupId, TransactionType.Income,  300m, "E1", new DateOnly(2026, 5, 1), userId),
            new GroupTransactionEntity(groupId, TransactionType.Expense, 100m, "S1", new DateOnly(2026, 5, 2), userId, TransactionCategory.AluguelDeQuadra),
            // Junho: +50 entrada → saldo = +50, acumulado = +250
            new GroupTransactionEntity(groupId, TransactionType.Income,   50m, "E2", new DateOnly(2026, 6, 1), userId));
        await db.SaveChangesAsync();

        var result = await sut.GetMonthlySummariesAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(2);

        var maio   = result.Data![0];
        var junho  = result.Data![1];

        maio.TotalIncome.Should().Be(300m);
        maio.TotalExpense.Should().Be(100m);
        maio.NetBalance.Should().Be(200m);
        maio.AccumulatedBalance.Should().Be(200m);

        junho.TotalIncome.Should().Be(50m);
        junho.NetBalance.Should().Be(50m);
        junho.AccumulatedBalance.Should().Be(250m, "200 de Maio + 50 de Junho");
    }

    [Fact]
    public async Task GetMonthlySummaries_NegativeMonth_ShouldReflectInAccumulated()
    {
        await using var db = DbContextFactory.Create(nameof(GetMonthlySummaries_NegativeMonth_ShouldReflectInAccumulated));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        db.GroupTransactions.AddRange(
            // Maio: -200 saída (mais saída que entrada)
            new GroupTransactionEntity(groupId, TransactionType.Income,   50m, "E1", new DateOnly(2026, 5, 1), userId),
            new GroupTransactionEntity(groupId, TransactionType.Expense, 200m, "S1", new DateOnly(2026, 5, 2), userId, TransactionCategory.Arbitragem),
            // Junho: +300
            new GroupTransactionEntity(groupId, TransactionType.Income,  300m, "E2", new DateOnly(2026, 6, 1), userId));
        await db.SaveChangesAsync();

        var result = await sut.GetMonthlySummariesAsync(groupId);

        var maio  = result.Data![0];
        var junho = result.Data![1];

        maio.NetBalance.Should().Be(-150m);
        maio.AccumulatedBalance.Should().Be(-150m);
        junho.AccumulatedBalance.Should().Be(150m, "-150 + 300");
    }

    // ── CreateManualAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateManual_ZeroAmount_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(CreateManual_ZeroAmount_ShouldFail));
        var sut = MakeSut(db);
        var dto = new BratnavaFC.Domain.Dtos.Transactions.CreateTransactionDto(
            TransactionType.Income, 0m, "desc", new DateOnly(2026, 5, 1), null);

        var result = await sut.CreateManualAsync(Guid.NewGuid(), dto, Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateManual_ExpenseWithoutCategory_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(CreateManual_ExpenseWithoutCategory_ShouldFail));
        var sut = MakeSut(db);
        var dto = new BratnavaFC.Domain.Dtos.Transactions.CreateTransactionDto(
            TransactionType.Expense, 100m, "Aluguel", new DateOnly(2026, 5, 1), null);

        var result = await sut.CreateManualAsync(Guid.NewGuid(), dto, Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateManual_HappyPath_ShouldPersistAndReturnDto()
    {
        await using var db = DbContextFactory.Create(nameof(CreateManual_HappyPath_ShouldPersistAndReturnDto));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var dto     = new BratnavaFC.Domain.Dtos.Transactions.CreateTransactionDto(
            TransactionType.Expense, 250m, "Arbitragem do jogo",
            new DateOnly(2026, 5, 15), TransactionCategory.Arbitragem);

        var result = await sut.CreateManualAsync(groupId, dto, userId);

        result.Success.Should().BeTrue();
        result.Data!.Amount.Should().Be(250m);
        result.Data.IsAutomatic.Should().BeFalse();
        result.Data.Category.Should().Be(TransactionCategory.Arbitragem);
        (await db.GroupTransactions.CountAsync()).Should().Be(1);
    }

    // ── DeleteManualAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteManual_AutomaticTransaction_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteManual_AutomaticTransaction_ShouldFail));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        // Cria uma transação automática
        var tx = new GroupTransactionEntity(
            groupId, 100m, "desc", new DateOnly(2026, 5, 1),
            TransactionSourceType.MonthlyPayment, sourceId, null);
        db.GroupTransactions.Add(tx);
        await db.SaveChangesAsync();

        var result = await sut.DeleteManualAsync(groupId, tx.Id);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("automático");
        (await db.GroupTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteManual_ManualTransaction_ShouldRemove()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteManual_ManualTransaction_ShouldRemove));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var tx = new GroupTransactionEntity(
            groupId, TransactionType.Income, 100m, "desc",
            new DateOnly(2026, 5, 1), userId);
        db.GroupTransactions.Add(tx);
        await db.SaveChangesAsync();

        var result = await sut.DeleteManualAsync(groupId, tx.Id);

        result.Success.Should().BeTrue();
        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteManual_NotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteManual_NotFound_ShouldFail));
        var sut = MakeSut(db);

        var result = await sut.DeleteManualAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    // ── RecordOrRemovePaymentEntryAsync — atualização de valor ───────────────

    [Fact]
    public async Task RecordOrRemove_AmountChangedAfterPayment_ShouldUpdateEntry()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_AmountChangedAfterPayment_ShouldUpdateEntry));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        // Pagamento criado a R$100
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "Mensalidade", new DateOnly(2026, 5, 1), isPaid: true, null);

        (await db.GroupTransactions.SingleAsync()).Amount.Should().Be(100m);

        // Desconto aplicado → valor efetivo cai para R$70
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            70m, "Mensalidade", new DateOnly(2026, 5, 1), isPaid: true, null);

        var tx = await db.GroupTransactions.SingleAsync();
        tx.Amount.Should().Be(70m, "deve atualizar para o novo valor efetivo");
        tx.SourceId.Should().Be(sourceId, "SourceId deve ser mantido");
    }

    [Fact]
    public async Task RecordOrRemove_FullDiscountAfterPayment_ShouldRemoveEntry()
    {
        await using var db = DbContextFactory.Create(nameof(RecordOrRemove_FullDiscountAfterPayment_ShouldRemoveEntry));
        var sut      = MakeSut(db);
        var groupId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        // Pago primeiro
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            100m, "Mensalidade", new DateOnly(2026, 5, 1), isPaid: true, null);

        (await db.GroupTransactions.CountAsync()).Should().Be(1);

        // Desconto total → valor efetivo = 0 → deve remover
        await sut.RecordOrRemovePaymentEntryAsync(
            groupId, TransactionSourceType.MonthlyPayment, sourceId,
            0m, "Mensalidade", new DateOnly(2026, 5, 1), isPaid: true, null);

        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    // ── SyncPaidPaymentsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task Sync_NoOrphans_ShouldReturnZeroCreated()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_NoOrphans_ShouldReturnZeroCreated));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();

        // Mensalidade paga COM lançamento no caixa já existente
        var payment = new MonthlyPaymentEntity(groupId, Guid.NewGuid(), 2026, 5, 100m);
        payment.MarkAsPaid(null, null, null, null);
        db.MonthlyPayments.Add(payment);
        db.GroupTransactions.Add(new GroupTransactionEntity(
            groupId, 100m, "Mensalidade Maio/2026 – Jogador",
            new DateOnly(2026, 5, 1),
            TransactionSourceType.MonthlyPayment, payment.Id, null));
        await db.SaveChangesAsync();

        var result = await sut.SyncPaidPaymentsAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().Be(0);
        result.Data.Updated.Should().Be(0);
        (await db.GroupTransactions.CountAsync()).Should().Be(1, "não deve duplicar");
    }

    [Fact]
    public async Task Sync_OrphanPaidMonthly_ShouldCreateTransaction()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_OrphanPaidMonthly_ShouldCreateTransaction));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();

        // Mensalidade paga SEM lançamento (simulando o bug da 500)
        var payment = new MonthlyPaymentEntity(groupId, Guid.NewGuid(), 2026, 5, 100m);
        payment.MarkAsPaid(null, null, null, null);
        db.MonthlyPayments.Add(payment);
        await db.SaveChangesAsync();

        (await db.GroupTransactions.CountAsync()).Should().Be(0, "pré-condição: nenhum lançamento");

        var result = await sut.SyncPaidPaymentsAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().Be(1);
        var tx = await db.GroupTransactions.SingleAsync();
        tx.SourceType.Should().Be(TransactionSourceType.MonthlyPayment);
        tx.SourceId.Should().Be(payment.Id);
        tx.Amount.Should().Be(100m);
        tx.IsAutomatic.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_IsIdempotent_CallTwiceCreatesNoExtras()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_IsIdempotent_CallTwiceCreatesNoExtras));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();

        var payment = new MonthlyPaymentEntity(groupId, Guid.NewGuid(), 2026, 5, 100m);
        payment.MarkAsPaid(null, null, null, null);
        db.MonthlyPayments.Add(payment);
        await db.SaveChangesAsync();

        await sut.SyncPaidPaymentsAsync(groupId); // 1ª chamada cria 1
        await sut.SyncPaidPaymentsAsync(groupId); // 2ª chamada não cria mais

        (await db.GroupTransactions.CountAsync()).Should().Be(1, "idempotente");
    }

    [Fact]
    public async Task Sync_ZeroEffectiveAmount_ShouldSkip()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_ZeroEffectiveAmount_ShouldSkip));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();

        // Pagamento com desconto total (amount=100, discount=100 → effective=0)
        // ApplyDiscount o marcará como Paid automaticamente
        var payment = new MonthlyPaymentEntity(groupId, Guid.NewGuid(), 2026, 5, 100m);
        payment.ApplyDiscount(100m, "desconto total", Guid.NewGuid());
        db.MonthlyPayments.Add(payment);
        await db.SaveChangesAsync();

        payment.Status.Should().Be(PaymentStatus.Paid, "pré-condição");

        var result = await sut.SyncPaidPaymentsAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().Be(0, "desconto total não deve gerar lançamento no caixa");
        (await db.GroupTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Sync_StaleAmount_ShouldUpdateEntry()
    {
        await using var db = DbContextFactory.Create(nameof(Sync_StaleAmount_ShouldUpdateEntry));
        var sut     = MakeSut(db);
        var groupId = Guid.NewGuid();

        // Cenário: pagamento foi criado (R$100), depois desconto de R$30 foi aplicado,
        // mas o lançamento no caixa ficou com o valor antigo (R$100) — valor efetivo deveria ser R$70
        var payment = new MonthlyPaymentEntity(groupId, Guid.NewGuid(), 2026, 5, 100m);
        payment.MarkAsPaid(null, null, null, null);
        payment.ApplyDiscount(30m, "desconto parcial", Guid.NewGuid()); // Discount=30, Status já era Paid
        db.MonthlyPayments.Add(payment);

        // Lançamento desatualizado no caixa (valor antigo antes do desconto)
        db.GroupTransactions.Add(new GroupTransactionEntity(
            groupId, 100m, "Mensalidade Maio/2026 – Jogador",
            new DateOnly(2026, 5, 1),
            TransactionSourceType.MonthlyPayment, payment.Id, null));
        await db.SaveChangesAsync();

        var result = await sut.SyncPaidPaymentsAsync(groupId);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().Be(0);
        result.Data.Updated.Should().Be(1, "deve detectar e corrigir o valor desatualizado");

        var tx = await db.GroupTransactions.SingleAsync();
        tx.Amount.Should().Be(70m, "deve refletir o valor efetivo após desconto");
    }
}
