namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class BulkExtraChargeDiscountDto
{
    public decimal  Discount       { get; init; }
    public string?  DiscountReason { get; init; }
    public Guid[]   PlayerIds      { get; init; } = [];
}
