namespace BratnavaFC.Domain.Entities;

public class PlayerEntity : InactivatableEntity
{
    public string Name { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public UserEntity User { get; private set; } = null!;
    public Guid GroupId { get; private set; }
    public GroupEntity Group { get; private set; } = null!;
    public decimal SkillPoints { get; private set; }
    public bool IsGoalkeeper { get; private set; }

    private readonly List<MatchPlayerEntity> _matchPlayers = new();
    public IReadOnlyCollection<MatchPlayerEntity> MatchPlayers => _matchPlayers;

    // EF
    private PlayerEntity() { }

    public PlayerEntity(string name, Guid userId, Guid groupId, decimal skillPoints, bool isGoalkeeper, BratnavaFC.Domain.Enums.Status status)
    {
        Rename(name);
        SetUser(userId);
        SetGroup(groupId);
        SetSkillPoints(skillPoints);
        SetGoalkeeper(isGoalkeeper);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Player name is required.");

        Name = name.Trim();
        Touch();
    }

    public void SetUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new InvalidOperationException("UserId is required.");

        UserId = userId;
        Touch();
    }

    public void SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId is required.");

        GroupId = groupId;
        Touch();
    }

    public void SetSkillPoints(decimal skillPoints)
    {
        if (skillPoints < 0)
            throw new InvalidOperationException("SkillPoints cannot be negative.");

        SkillPoints = skillPoints;
        Touch();
    }

    public void SetGoalkeeper(bool isGoalkeeper)
    {
        IsGoalkeeper = isGoalkeeper;
        Touch();
    }
}
