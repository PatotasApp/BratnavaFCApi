using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class MonthlyPaymentCellDto
{
    public int           Month          { get; init; }
    public PaymentStatus Status         { get; init; }
    public decimal       Amount         { get; init; }
    public decimal       Discount       { get; init; }
    public string?       DiscountReason { get; init; }
    public DateTime?     PaidAt         { get; init; }
    public Guid?         MarkedByUserId { get; init; }
    public string?       MarkedByUserName { get; init; }
    public string?       MarkedByUserKind { get; init; }
    public bool          HasProof       { get; init; }
    public string?       ProofFileName  { get; init; }
}
