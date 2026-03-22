namespace BratnavaFC.Domain.Entities;
public class PollVoteEntity : BaseEntity
{
    public Guid PollId { get; private set; }
    public Guid OptionId { get; private set; }
    public Guid PlayerId { get; private set; }
    public PollOptionEntity? Option { get; private set; }

    private PollVoteEntity() { }

    public PollVoteEntity(Guid pollId, Guid optionId, Guid playerId)
    {
        PollId = pollId;
        OptionId = optionId;
        PlayerId = playerId;
    }
}
