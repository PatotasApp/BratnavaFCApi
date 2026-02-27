namespace BratnavaFC.Domain.Entities;

public class PlayerEntity : InactivatableEntity
{
    public string Name { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public UserEntity? User { get; private set; }
    public Guid GroupId { get; private set; }
    public GroupEntity Group { get; private set; } = null!;
    public decimal SkillPoints { get; private set; }
    public bool IsGoalkeeper { get; private set; }
    public bool IsGuest { get; private set; }

    private readonly List<MatchPlayerEntity> _matchPlayers = new();
    public IReadOnlyCollection<MatchPlayerEntity> MatchPlayers => _matchPlayers;

    // EF
    private PlayerEntity() { }

    public PlayerEntity(string name, Guid? userId, Guid groupId, decimal skillPoints, bool isGoalkeeper, bool isGuest = false, BratnavaFC.Domain.Enums.Status status = Enums.Status.Active)
    {
        Rename(name);
        if (userId.HasValue) SetUser(userId.Value);
        SetGroup(groupId);
        SetSkillPoints(skillPoints);
        SetGoalkeeper(isGoalkeeper);
        SetIsGuest(isGuest);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Player name is required.");

        Name = name.Trim();
    }

    public void SetUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new InvalidOperationException("UserId is required.");

        UserId = userId;
    }

    public void SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId is required.");

        GroupId = groupId;
    }

    public void SetSkillPoints(decimal skillPoints)
    {
        if (skillPoints < 0)
            throw new InvalidOperationException("SkillPoints cannot be negative.");

        SkillPoints = skillPoints;
    }

    public void SetGoalkeeper(bool isGoalkeeper)
    {
        IsGoalkeeper = isGoalkeeper;
    }

    public void SetIsGuest(bool isGuest)
    {
        IsGuest = isGuest;
    }
}
