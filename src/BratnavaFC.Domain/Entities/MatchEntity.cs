namespace BratnavaFC.Domain.Entities;

public class MatchEntity : BaseEntity
{
    //  EF Core
    private MatchEntity() { }

    public MatchEntity(DateTime playedAt, string placeName)
    {
        PlayedAt = playedAt;
        PlaceName = placeName ?? throw new ArgumentNullException(nameof(placeName));
    }

    public DateTime PlayedAt { get; private set; }
    public int? TeamAGoals { get; private set; }
    public int? TeamBGoals { get; private set; }
    public string PlaceName { get; private set; } = string.Empty;

    public List<MatchPlayerEntity> Players { get; private set; } = [];
    public List<VoteEntity> Votes { get; private set; } = [];

    public bool IsFinalized { get; private set; } = false;

    public IReadOnlyList<MatchPlayerEntity> TeamAPlayers => Players.Where(p => p.Team == 1).ToList();
    public IReadOnlyList<MatchPlayerEntity> TeamBPlayers => Players.Where(p => p.Team == 2).ToList();

    public void SetPlaceName(string placeName)
    {
        PlaceName = placeName;
        UpdateDate = DateTime.UtcNow;
    }

    public void SetPlayedAt(DateTime playedAt)
    {
        PlayedAt = playedAt;
        UpdateDate = DateTime.UtcNow;
    }

    public void SetScore(int homeGoals, int awayGoals)
    {
        TeamAGoals = homeGoals;
        TeamBGoals = awayGoals;
        UpdateDate = DateTime.UtcNow;
    }

    public void AddPlayer(MatchPlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);

        short playerTeam = player.Team;

        player.AssignToMatch(this);
        Players.Add(player);
        UpdateDate = DateTime.UtcNow;
    }

    public bool RemovePlayer(MatchPlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var removed = Players.Remove(player);
        if (removed) UpdateDate = DateTime.UtcNow;
        return removed;
    }

    public void MarkFinalized()
    {
        if (IsFinalized) return;
        IsFinalized = true;
        UpdateDate = DateTime.UtcNow;
    }
}