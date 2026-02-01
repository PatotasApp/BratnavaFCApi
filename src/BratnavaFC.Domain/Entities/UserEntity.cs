using System;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public sealed class UserEntity : BaseEntity
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTimeOffset? BirthDate { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string? Phone { get; set; }
    public List<GroupEntity> Groups { get; set; } = [];
    public List<PlayerEntity> Players { get; set; } = [];
    public Status Status { get; set; }
}
