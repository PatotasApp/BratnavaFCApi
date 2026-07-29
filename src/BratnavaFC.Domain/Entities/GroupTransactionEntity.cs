using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Representa um lançamento financeiro do caixa do grupo.
/// Pode ser gerado automaticamente quando um pagamento é confirmado,
/// ou inserido manualmente pelo financeiro.
/// </summary>
public sealed class GroupTransactionEntity : BaseEntity
{
    // EF Core
    private GroupTransactionEntity() { }

    /// <summary>Cria uma transação manual (Income ou Expense).</summary>
    public GroupTransactionEntity(
        Guid                groupId,
        TransactionType     type,
        decimal             amount,
        string              description,
        DateOnly            date,
        Guid                createdByUserId,
        TransactionCategory? category = null)
    {
        if (groupId == Guid.Empty)     throw new ArgumentException("GroupId é obrigatório.");
        if (amount  <= 0)              throw new ArgumentException("Amount deve ser positivo.");
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Descrição é obrigatória.");
        if (type == TransactionType.Expense && category is null)
            throw new ArgumentException("Categoria é obrigatória em saídas.");

        GroupId         = groupId;
        Type            = type;
        Amount          = amount;
        Description     = description;
        Date            = date;
        Category        = category;
        IsAutomatic     = false;
        SourceType      = TransactionSourceType.Manual;
        CreatedByUserId = createdByUserId;
    }

    /// <summary>Cria uma transação automática originada de um pagamento confirmado.</summary>
    public GroupTransactionEntity(
        Guid                    groupId,
        decimal                 amount,
        string                  description,
        DateOnly                date,
        TransactionSourceType   sourceType,
        Guid                    sourceId,
        string?                 playerName = null)
    {
        if (groupId == Guid.Empty) throw new ArgumentException("GroupId é obrigatório.");
        if (amount  <= 0)          throw new ArgumentException("Amount deve ser positivo.");
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Descrição é obrigatória.");

        GroupId    = groupId;
        Type       = TransactionType.Income;
        Amount     = amount;
        Description = description;
        Date       = date;
        Category   = null;   // income não tem categoria
        IsAutomatic = true;
        SourceType  = sourceType;
        SourceId    = sourceId;
        PlayerName  = playerName;
    }

    public Guid    GroupId          { get; private set; }
    public TransactionType       Type        { get; private set; }
    public decimal Amount          { get; private set; }
    public string  Description     { get; private set; } = default!;
    public DateOnly Date           { get; private set; }

    /// <summary>Null para entradas; obrigatório para saídas manuais.</summary>
    public TransactionCategory?  Category    { get; private set; }

    public bool                  IsAutomatic { get; private set; }
    public TransactionSourceType SourceType  { get; private set; }

    /// <summary>Id do MonthlyPayment ou ExtraChargePayment que originou a entrada automática.</summary>
    public Guid?   SourceId        { get; private set; }

    /// <summary>Nome do jogador que pagou (preenchido apenas em transações automáticas).</summary>
    public string? PlayerName      { get; private set; }

    /// <summary>Usuário que criou a transação manual (null em automáticas).</summary>
    public Guid?   CreatedByUserId { get; private set; }

    public void ClearCreator()
    {
        CreatedByUserId = null;
    }
}
