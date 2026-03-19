namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class PaymentSummaryDto
{
    public bool                   HasPendingMonthly    { get; init; }
    public int                    PendingMonthsCount   { get; init; }
    public ExtraChargePendingDto[] PendingExtraCharges { get; init; } = [];

    public bool IsUpToDate => !HasPendingMonthly && PendingExtraCharges.Length == 0;
}
