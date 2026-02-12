namespace BratnavaFC.Domain.Entities;

public class PlayerEntity : InactivatableEntity
{
    public string Name { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public Guid GroupId { get; set; }
    public GroupEntity Group { get; set; } = null!;
    public decimal SkillPoints { get; set; } = 0;
    public bool IsGoalkeeper { get; set; }

    public List<MatchPlayerEntity> MatchPlayers { get; private set; } = new();
}
