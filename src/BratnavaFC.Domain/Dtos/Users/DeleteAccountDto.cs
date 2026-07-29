namespace BratnavaFC.Domain.Dtos.Users;

public sealed class DeleteAccountDto
{
    public string Password { get; set; } = null!;
    public string Confirmation { get; set; } = null!;
    public bool ForceWithoutPayment { get; set; }
}
