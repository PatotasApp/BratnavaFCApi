using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Transactions;

/// <summary>Payload para criar uma transação manual (entrada ou saída).</summary>
public sealed record CreateTransactionDto(
    TransactionType      Type,
    decimal              Amount,
    string               Description,
    DateOnly             Date,
    TransactionCategory? Category);

/// <summary>Representa uma transação individual retornada pela API.</summary>
public sealed record TransactionDto(
    Guid                  Id,
    TransactionType       Type,
    decimal               Amount,
    string                Description,
    DateOnly              Date,
    TransactionCategory?  Category,
    bool                  IsAutomatic,
    TransactionSourceType SourceType,
    string?               PlayerName,
    DateTime              CreatedAt);

/// <summary>Resumo financeiro de um mês.</summary>
public sealed record TransactionMonthSummaryDto(
    int     Year,
    int     Month,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetBalance,
    decimal AccumulatedBalance);

/// <summary>Total de pendências em aberto (independente do mês).</summary>
public sealed record PendingTotalsDto(
    decimal TotalMonthlyPending,
    decimal TotalExtraChargesPending,
    decimal GrandTotal);

/// <summary>Resultado da sincronização de pagamentos pagos x caixa.</summary>
public sealed record SyncResultDto(int Created, int Updated = 0);
