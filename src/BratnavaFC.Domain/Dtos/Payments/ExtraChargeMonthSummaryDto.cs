namespace BratnavaFC.Domain.Dtos.Payments;

/// <summary>Status agregado das cobranças extras de um mês — usado nos badges do seletor de meses.</summary>
public sealed class ExtraChargeMonthSummaryDto
{
    public int  Month      { get; set; }
    public int  Count      { get; set; }
    public bool AllPaid    { get; set; }
    public bool HasPending { get; set; }
}
