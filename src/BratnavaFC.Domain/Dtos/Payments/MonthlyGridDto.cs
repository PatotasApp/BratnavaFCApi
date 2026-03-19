namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class MonthlyGridDto
{
    public int                      Year       { get; init; }
    public decimal?                 MonthlyFee { get; init; }
    public PlayerMonthlyRowDto[]    Players    { get; init; } = [];
}
