namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class UpdateExtraChargeDetailsDto
{
    public string?  Name        { get; set; }  // null = no change
    public string?  Description { get; set; }  // null = no change; "" = clear
    public decimal? Amount      { get; set; }  // null = no change
}
