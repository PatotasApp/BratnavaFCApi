namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class ExtraChargePendingDto
{
    public Guid      ChargeId    { get; init; }
    public string    ChargeName  { get; init; } = string.Empty;
    public decimal   Amount      { get; init; }
    public decimal   Discount    { get; init; }
    public decimal   FinalAmount { get; init; }
    public DateOnly? DueDate     { get; init; }
}
