namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class CreateExtraChargeDto
{
    public string    Name        { get; set; } = string.Empty;
    public string?   Description { get; set; }
    public decimal   Amount      { get; set; }
    public DateOnly? DueDate     { get; set; }
    public Guid[]    PlayerIds   { get; set; } = [];
}
