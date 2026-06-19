namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class AddPollGuestDto
{
    public string GuestName { get; set; } = "";
    public bool   IsAdult   { get; set; } = true;
}
