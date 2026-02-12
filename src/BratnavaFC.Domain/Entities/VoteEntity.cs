namespace BratnavaFC.Domain.Entities;

public class VoteEntity : BaseEntity
{
    // EF Core
    private VoteEntity() { }

    public VoteEntity(Guid matchId, Guid voterId, Guid votedForId)
    {
        MatchId = matchId;
        VoterId = voterId;
        VotedForId = votedForId;
    }

    public Guid MatchId { get; private set; }
    public Guid VoterId { get; private set; }
    public Guid VotedForId { get; private set; }

    public MatchEntity? Match { get; private set; }
    public MatchPlayerEntity? Voter { get; private set; }
    public MatchPlayerEntity? VotedFor { get; private set; }
}
