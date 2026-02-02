using System;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class GroupEntity : BaseEntity
{
    public string Name { get; set; }
    public DateTimeOffset? ScheduleMatchDate { get; set; }
    public List<PlayerEntity>? Players { get; set; }
    public List<GroupAdminEntity> Admins { get; set; } = [];
    public Status Status { get; set; }
}
