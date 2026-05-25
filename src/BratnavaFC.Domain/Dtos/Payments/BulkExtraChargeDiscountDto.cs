namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class BulkExtraChargeDiscountDto
{
    public decimal  Discount       { get; init; }
    public string?  DiscountReason { get; init; }
    public Guid[]   PlayerIds      { get; init; } = [];

    /// <summary>
    /// Se true, além de aplicar o desconto, marca como Pago os pagamentos que
    /// continuarem pendentes após o desconto (desconto parcial).
    /// Pagamentos auto-pagos por desconto total (Discount ≥ Amount) já ficam Paid
    /// independentemente desse campo.
    /// </summary>
    public bool MarkAsPaid { get; init; }
}
