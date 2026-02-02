using System;
using System.Collections.Generic;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public enum UserRole
{
    User = 1,
    Admin = 2,
    GodMode = 3
}

public sealed class UserEntity : BaseEntity
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public DateTimeOffset? BirthDate { get; set; }
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string? Phone { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public List<GroupEntity> Groups { get; set; } = [];
    public List<PlayerEntity> Players { get; set; } = [];

    public Status Status { get; set; }
}
