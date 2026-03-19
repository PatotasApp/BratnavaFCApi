namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class ExtraChargeDto
{
    public Guid                     Id          { get; init; }
    public string                   Name        { get; init; } = string.Empty;
    public string?                  Description { get; init; }
    public decimal                  Amount      { get; init; }
    public DateOnly?                DueDate     { get; init; }
    public DateTime                 CreatedAt   { get; init; }
    public bool                     IsCancelled { get; init; }
    public ExtraChargePaymentDto[]  Payments    { get; init; } = [];
}
