namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class PollMemberVoteDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public List<Guid> VotedOptionIds { get; set; } = new();
    public DateTime? VotedAt { get; set; }
}
