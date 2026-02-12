namespace BratnavaFC.Domain.Entities;

public class GroupEntity : InactivatableEntity
{
    public string Name { get; private set; } = null!;
    public DateTimeOffset? ScheduleMatchDate { get; private set; }

    private readonly List<PlayerEntity> _players = [];
    public IReadOnlyCollection<PlayerEntity> Players => _players;

    private readonly List<GroupAdminEntity> _admins = [];
    public IReadOnlyCollection<GroupAdminEntity> Admins => _admins;

    // EF
    private GroupEntity() { }

    public GroupEntity(string name, DateTimeOffset? scheduleMatchDate)
    {
        Rename(name);
        Reschedule(scheduleMatchDate);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Group name is required.");

        Name = name.Trim();
        Touch();
    }

    public void Reschedule(DateTimeOffset? scheduleMatchDate)
    {
        ScheduleMatchDate = scheduleMatchDate;
        Touch();
    }

    public void SetAdmins(IEnumerable<Guid> userAdminIds)
    {
        if (userAdminIds == null) throw new InvalidOperationException("Admins list is required.");

        _admins.Clear();

        foreach (var adminId in userAdminIds.Distinct())
        {
            _admins.Add(new GroupAdminEntity
            {
                GroupId = Id,
                UserId = adminId
            });
        }

        Touch();
    }
}
