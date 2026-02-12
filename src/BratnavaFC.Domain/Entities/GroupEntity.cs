namespace BratnavaFC.Domain.Entities;

public class GroupEntity : InactivatableEntity
{
    public string Name { get; set; }
    public DateTimeOffset? ScheduleMatchDate { get; set; }
    public List<PlayerEntity>? Players { get; set; }
    public List<GroupAdminEntity> Admins { get; set; } = [];
}
