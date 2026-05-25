using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Transactions;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Application.Abstractions;

public interface IFinancialTransactionService
{
    // ── Consultas ────────────────────────────────────────────────────────────

    /// <summary>Retorna todas as transações de um mês/ano específico.</summary>
    Task<Result<IReadOnlyList<TransactionDto>>> GetByMonthAsync(
        Guid groupId, int year, int month, CancellationToken ct = default);

    /// <summary>Retorna o resumo mês a mês (acumulado) de todo o histórico do grupo.</summary>
    Task<Result<IReadOnlyList<TransactionMonthSummaryDto>>> GetMonthlySummariesAsync(
        Guid groupId, CancellationToken ct = default);

    /// <summary>Retorna o total de mensalidades e cobranças extras pendentes em aberto.</summary>
    Task<Result<PendingTotalsDto>> GetPendingTotalsAsync(
        Guid groupId, CancellationToken ct = default);

    // ── Escrita ──────────────────────────────────────────────────────────────

    /// <summary>Cria uma transação manual (income ou expense) pelo financeiro.</summary>
    Task<Result<TransactionDto>> CreateManualAsync(
        Guid groupId,
        CreateTransactionDto dto,
        Guid createdByUserId,
        CancellationToken ct = default);

    /// <summary>Exclui uma transação manual. Transações automáticas não podem ser excluídas diretamente.</summary>
    Task<Result> DeleteManualAsync(
        Guid groupId, Guid transactionId, CancellationToken ct = default);

    // ── Integração com pagamentos (chamado internamente pelo PaymentService) ──

    /// <summary>
    /// Registra ou remove a entrada automática associada a um pagamento.
    /// Se <paramref name="isPaid"/> = true e não existe entrada → cria.
    /// Se <paramref name="isPaid"/> = false e existe entrada → remove.
    /// Idempotente.
    /// </summary>
    Task RecordOrRemovePaymentEntryAsync(
        Guid                  groupId,
        TransactionSourceType sourceType,
        Guid                  sourceId,
        decimal               amount,
        string                description,
        DateOnly              date,
        bool                  isPaid,
        string?               playerName,
        CancellationToken     ct = default);

    /// <summary>
    /// Varre todos os pagamentos marcados como Pago que ainda não têm lançamento
    /// no caixa e os cria. Usado para sincronizar retroativamente.
    /// </summary>
    Task<Result<SyncResultDto>> SyncPaidPaymentsAsync(
        Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Remove TODOS os lançamentos do caixa do grupo (automáticos e manuais).
    /// Usar apenas para fins de diagnóstico/teste antes de um sync.
    /// </summary>
    Task<Result<int>> ClearAllTransactionsAsync(
        Guid groupId, CancellationToken ct = default);
}
