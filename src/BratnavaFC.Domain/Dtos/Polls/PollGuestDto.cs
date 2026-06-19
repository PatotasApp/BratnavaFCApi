namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class PollGuestDto
{
    public Guid   Id              { get; set; }
    public Guid   VoterPlayerId   { get; set; }
    public string VoterPlayerName { get; set; } = "";
    public string GuestName       { get; set; } = "";
    public bool   IsAdult         { get; set; }
}
