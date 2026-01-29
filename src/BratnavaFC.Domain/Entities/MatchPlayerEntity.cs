namespace BratnavaFC.Domain.Entities;

public class MatchPlayerEntity : BaseEntity
{
    // EF Core
    private MatchPlayerEntity() { }

    public MatchPlayerEntity(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; private set; } = null!;
    public bool? IsMvp { get; private set; }

    public Guid MatchId { get; private set; }
    public MatchEntity? Match { get; private set; }

    public short Team { get; private set; }

    public List<VoteEntity> ReceivedVotes { get; private set; } = new();
    public Guid? VotedForId { get; private set; }

    public void SetName(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        UpdateDate = DateTime.UtcNow;
    }

    public void SetMvp()
    {
        IsMvp = true;
        UpdateDate = DateTime.UtcNow;
    }

    public void RevokeMvp()
    {
        IsMvp = null;
        UpdateDate = DateTime.UtcNow;
    }

    public void AssignToMatch(MatchEntity match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        MatchId = match.Id;
        UpdateDate = DateTime.UtcNow;
    }

    public void SetTeam(short team)
    {
        if (team != 1 && team != 2)
            throw new ArgumentOutOfRangeException(nameof(team), "Time deve ser 1 (Time A) ou 2 (Time B).");

        Team = team;
        UpdateDate = DateTime.UtcNow;
    }

    public void AddReceivedVote(VoteEntity vote)
    {
        ArgumentNullException.ThrowIfNull(vote);

        if (!ReceivedVotes.Exists(v => v.Id == vote.Id))
        {
            ReceivedVotes.Add(vote);
            UpdateDate = DateTime.UtcNow;
        }
    }

    public void RemoveReceivedVote(Guid voteId)
    {
        var idx = ReceivedVotes.FindIndex(v => v.Id == voteId);
        if (idx >= 0)
        {
            ReceivedVotes.RemoveAt(idx);
            UpdateDate = DateTime.UtcNow;
        }
    }

    public void SetVotedFor(Guid? votedForId)
    {
        VotedForId = votedForId;
        UpdateDate = DateTime.UtcNow;
    }

    public void ClearVotedFor()
    {
        VotedForId = null;
        UpdateDate = DateTime.UtcNow;
    }
}