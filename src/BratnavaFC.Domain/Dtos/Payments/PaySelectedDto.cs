namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class PaySelectedDto
{
    public PaySelectedItem[] Items { get; set; } = [];
}

public sealed class PaySelectedItem
{
    public PendingPaymentType Type  { get; set; }
    public int?  Year     { get; set; }
    public int?  Month    { get; set; }
    public Guid? ChargeId { get; set; }

    /// <summary>
    /// Estado desejado: true = marcar como pago, false = reverter para pendente.
    /// </summary>
    public bool IsPaid { get; set; }
}
