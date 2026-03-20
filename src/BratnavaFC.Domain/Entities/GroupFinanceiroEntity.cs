using System;

namespace BratnavaFC.Domain.Entities;

public class GroupFinanceiroEntity
{
    public UserEntity User { get; set; }
    public Guid UserId { get; set; }
    public GroupEntity Group { get; set; }
    public Guid GroupId { get; set; }
}
