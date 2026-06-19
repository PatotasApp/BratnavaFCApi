namespace BratnavaFC.Domain.Entities;

public class PollGuestEntity : BaseEntity
{
    public Guid   PollId        { get; private set; }
    public Guid   VoterPlayerId { get; private set; }
    public string GuestName     { get; private set; } = null!;
    public bool   IsAdult       { get; private set; }

    private PollGuestEntity() { }

    public PollGuestEntity(Guid pollId, Guid voterPlayerId, string guestName, bool isAdult)
    {
        if (string.IsNullOrWhiteSpace(guestName))
            throw new InvalidOperationException("Nome do convidado é obrigatório.");
        PollId        = pollId;
        VoterPlayerId = voterPlayerId;
        GuestName     = guestName.Trim();
        IsAdult       = isAdult;
    }
}
