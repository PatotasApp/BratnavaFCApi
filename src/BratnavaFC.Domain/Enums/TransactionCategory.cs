namespace BratnavaFC.Domain.Enums;

/// <summary>Categoria de uma saída manual. Usado apenas em transações do tipo Expense.</summary>
public enum TransactionCategory : short
{
    AluguelDeQuadra = 0,
    Arbitragem      = 1,
    Uniforme        = 2,
    Material        = 3,
    Outros          = 4,
}
