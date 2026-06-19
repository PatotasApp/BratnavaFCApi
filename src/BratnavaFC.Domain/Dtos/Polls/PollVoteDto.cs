namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class PollVoteDto
{
    public Guid              OptionId   { get; set; }
    public Guid              PlayerId   { get; set; }
    public string            PlayerName { get; set; } = "";
    public List<PollGuestDto> Guests    { get; set; } = new();
}
