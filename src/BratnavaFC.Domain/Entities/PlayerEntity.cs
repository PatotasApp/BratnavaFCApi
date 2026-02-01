using System;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class PlayerEntity : BaseEntity
{
    public string Name { get; set; }

    public Guid UserId { get; set; }
    public UserEntity User { get; set; }

    public Guid GroupId { get; set; }
    public GroupEntity Group { get; set; }

    public SoccerPosition MainPosition { get; set; }
    public SoccerPosition[] Positions { get; set; }
    public decimal SkillPoints { get; set; } = 0;

    //public List<PlayerGoal> Goals { get; set; }

    public Status Status { get; set; }
}
