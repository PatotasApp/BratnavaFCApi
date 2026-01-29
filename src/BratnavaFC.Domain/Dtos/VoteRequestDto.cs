namespace BratnavaFC.Domain.Dtos;

public class VoteRequestDto
{
    public Guid VoterPlayerId { get; set; }
    public Guid VotedPlayerId { get; set; }
}