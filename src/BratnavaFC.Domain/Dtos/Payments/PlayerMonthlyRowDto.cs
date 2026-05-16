namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class PlayerMonthlyRowDto
{
    public Guid   PlayerId     { get; init; }
    public Guid?  UserId       { get; init; }
    public string PlayerName   { get; init; } = string.Empty;
    public bool   IsGoalkeeper { get; init; }

    /// <summary>Ano em que o jogador entrou na patota.</summary>
    public int JoinedYear  { get; init; }
    /// <summary>Mês em que o jogador entrou na patota (1–12).</summary>
    public int JoinedMonth { get; init; }

    /// <summary>Células apenas para os meses a partir da entrada do jogador.</summary>
    public MonthlyPaymentCellDto[] Months { get; init; } = [];
}
