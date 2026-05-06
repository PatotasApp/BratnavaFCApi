namespace BratnavaFC.Domain.Entities;

public sealed class MatchBetEntity : BaseEntity
{
    public Guid GroupId  { get; private set; }
    public Guid MatchId  { get; private set; }
    public Guid UserId   { get; private set; }
    public bool IsResolved { get; private set; }

    private readonly List<MatchBetSelectionEntity> _selections = new();
    public IReadOnlyList<MatchBetSelectionEntity> Selections => _selections.AsReadOnly();

    public MatchBetEntity(Guid groupId, Guid matchId, Guid userId)
    {
        GroupId = groupId;
        MatchId = matchId;
        UserId  = userId;
    }

    public void ReplaceSelections(List<MatchBetSelectionEntity> selections)
    {
        _selections.Clear();
        _selections.AddRange(selections);
        UpdateDate = DateTime.UtcNow;
    }

    public void MarkResolved()
    {
        IsResolved = true;
        UpdateDate = DateTime.UtcNow;
    }

    public void Unresolve()
    {
        IsResolved = false;
        UpdateDate = DateTime.UtcNow;
    }

    // EF Core
    private MatchBetEntity() { }
}
