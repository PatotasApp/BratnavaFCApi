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
    public short Team { get; private set; }
    public MatchEntity? Match { get; private set; }

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
        IsMvp = false;
        UpdateDate = DateTime.UtcNow;
    }

    public void AssignToMatch(MatchEntity match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        Id = match.Id;
        UpdateDate = DateTime.UtcNow;
    }

    public void SetTeam(short team)
    {
        Team = team;
        UpdateDate = DateTime.UtcNow;
    }
}