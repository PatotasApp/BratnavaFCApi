namespace BratnavaFC.Domain.Entities;

public class MatchEntity : BaseEntity
{
    //  EF Core
    private MatchEntity() { }

    public MatchEntity(DateTime playedAt)
    {
        PlayedAt = playedAt;
    }

    public DateTime PlayedAt { get; private set; }
    public int TeamAGoals { get; private set; }
    public int TeamBGoals { get; private set; }

    public List<MatchPlayerEntity> Players { get; private set; } = [];

    public IReadOnlyList<MatchPlayerEntity> TeamAPlayers => Players.Where(p => p.Team == 1).ToList();
    public IReadOnlyList<MatchPlayerEntity> TeamBPlayers => Players.Where(p => p.Team == 2).ToList();

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
}